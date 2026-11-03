using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>
/// The main window's model (spec 6.1): prompt, tabs, F key row, palette, and status bar, driven by keybindings (spec 6.5).
/// Enter runs a command in the active tab, Shift+Enter in a new one.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IAsyncDisposable
{
    readonly FunctionRouter router;
    readonly IConfirm confirm;
    readonly SynchronizationContext? ui = SynchronizationContext.Current;
    readonly ChangeWatcher watcher;
    readonly CancellationTokenSource stopping = new();
    Task? watching;
    bool nextRunInNewTab;
    bool routing;
    string? lastUser;

    public ShellViewModel(ShellContext ctx, FunctionRouter router, IConfirm confirm)
    {
        Context = ctx;
        ctx.Confirm = confirm;
        ctx.Run = (command, newTab) => RunAsync(command, newTab);
        this.router = router;
        this.confirm = confirm;
        Tabs = new TabsViewModel(ReplayAsync, LoadAsync, confirm);
        Palette = new PaletteViewModel(command => RunAsync(command, newTab: false));
        FKeys = FKeyRow.From(ctx.Keys.Current);
        ctx.Keys.Changed += () => FKeys = FKeyRow.From(ctx.Keys.Current);
        ctx.CompanyChanged += () => OnPropertyChanged(nameof(Title));
        ctx.Reported += message => Status.Message = message;
        Session = new SessionViewModel(ctx, OnLoggedInAsync);
        watcher = new ChangeWatcher(ctx, Tabs, Post, LoadAsync);
        ctx.Host.StateChanged += _ => Post(ShowConnection);
        ctx.Host.Reconnected += () => Post(() =>
        {
            // Changes were missed while away, so every tab may be out of date.
            foreach (var tab in Tabs.Tabs) tab.Changed = true;
        });
        ctx.Settings.Changed += ShowWorkingDate;
        ctx.WorkingDateChanged += ShowWorkingDate;
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabsViewModel.Active)) ShowChanged();
        };
        Tabs.Tabs.CollectionChanged += (_, e) =>
        {
            foreach (TabViewModel tab in e.NewItems ?? Array.Empty<TabViewModel>())
                tab.PropertyChanged += (_, p) =>
                {
                    if (p.PropertyName == nameof(TabViewModel.Changed)) ShowChanged();
                };
        };
        ShowConnection();
        ShowWorkingDate();
    }

    public SessionViewModel Session { get; }

    /// <summary>False while a session panel covers the shell: no binding runs and typed letters do not reach the prompt.</summary>
    public bool AcceptsTyping => Session.Panel == SessionPanel.None;

    /// <summary>True while the active screen records a key: every key goes to it, bound or not.</summary>
    public bool WantsEveryKey => Tabs.Active?.Screen is IKeyRecorder { Recording: true };

    /// <summary>Starts following the change stream.</summary>
    public void Start() => watching ??= watcher.RunAsync(stopping.Token);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        if (watching is not null)
            try { await watching; }
            catch (OperationCanceledException) { }
        stopping.Dispose();
    }

    public ShellContext Context { get; }

    public PromptViewModel Prompt { get; } = new();

    public TabsViewModel Tabs { get; }

    public StatusBarViewModel Status { get; } = new();

    public PaletteViewModel Palette { get; }

    [ObservableProperty]
    public partial IReadOnlyList<FKeyLabel> FKeys { get; private set; }

    /// <summary>"Open Hisab Kitab: {Company} FY {yyyy-yy}" (spec 6.1).</summary>
    public string Title => Context.Company is { } c && Context.Year is { } y
        ? string.Format(CultureInfo.InvariantCulture, Text.TitleWithCompany, c.Name, y.Label)
        : Text.TitleNoCompany;

    public event Action? FocusPromptRequested;

    /// <summary>A document to print or save, with its PDF file name. The window shows the print or save dialog.</summary>
    public event Action<Printing.PrintDocument, string, Printing.PrintTarget>? PrintRequested;

    /// <summary>The window reports how a print or save ended.</summary>
    public void Printed(string message) => Status.Message = message;

    async Task PrintAsync(Printing.PrintTarget target)
    {
        if (Tabs.Active?.Screen is not Printing.IPrintable screen)
        {
            Status.Message = Text.NothingToPrint;
            return;
        }
        var result = await screen.PrintAsync(target);
        if (result.Refusal is not null || result.Document is not { } doc)
        {
            Status.Message = result.Refusal;
            return;
        }
        // A PDF never shows empty boxes for a name its fonts lack (owner decision, 2026-11-03); paper shapes it correctly.
        if (target == Printing.PrintTarget.Pdf && Printing.PdfText.FirstUnsupported(doc) is { } text)
        {
            Status.Message = string.Format(CultureInfo.InvariantCulture, Text.PdfCannotShow, text);
            return;
        }
        PrintRequested?.Invoke(doc, result.FileName, target);
    }

    public async Task RunAsync(string input, bool newTab)
    {
        // Enter pressed again while a command is still asking the host routes nothing more.
        if (routing) return;
        routing = true;
        try
        {
            Status.Message = null;
            var route = await router.RouteAsync(input, Context);
            var inNewTab = newTab || nextRunInNewTab;
            if (route.Screen is { } kept && !inNewTab && Tabs.Active is { Screen.HasUnsavedEdits: true } && !await confirm.AskAsync(Text.DiscardChanges))
            {
                (kept as IDisposable)?.Dispose();
                return;   // the typed command stays in the prompt
            }
            nextRunInNewTab = false;
            Prompt.Text = "";
            if (route.Status is { } status) Status.Message = status;
            if (route.Refresh)
            {
                await RefreshAsync();
                return;
            }
            if (route.Print is { } target)
            {
                await PrintAsync(target);
                return;
            }
            if (route.Screen is not { } screen) return;
            Tabs.Show(screen, route.Command!, inNewTab);
            await LoadAsync(screen);
        }
        finally { routing = false; }
    }

    async Task<IScreen?> ReplayAsync(string command)
    {
        Context.Replaying = true;
        try { return (await router.RouteAsync(command, Context)).Screen; }
        finally { Context.Replaying = false; }
    }

    /// <summary>True when a binding ran.</summary>
    public async Task<bool> HandleKeyAsync(KeyChord key, IReadOnlySet<string> context)
    {
        if (!AcceptsTyping) return false;
        if (Tabs.Active?.Screen is IKeyRecorder { Recording: true } recorder)
        {
            await recorder.RecordKeyAsync(key);
            return true;
        }
        var command = Context.Keys.Current.Resolve(key, context);
        return command is not null && await RunCommandAsync(command, context);
    }

    /// <summary>Runs a command by its id, as a key or a menu item does. True when the id is known.</summary>
    public async Task<bool> RunCommandAsync(string command, IReadOnlySet<string>? context = null)
    {
        context ??= new HashSet<string>();
        if (command.StartsWith("run.", StringComparison.Ordinal))
        {
            await RunAsync(command[4..], newTab: false);
            return true;
        }
        if (command.StartsWith("tab.goto.", StringComparison.Ordinal))
        {
            Tabs.GoTo(int.Parse(command["tab.goto.".Length..], CultureInfo.InvariantCulture));
            return true;
        }
        switch (command)
        {
            case "prompt.focus": FocusPromptRequested?.Invoke(); break;
            case "prompt.run": await RunAsync(Prompt.Text, newTab: false); break;
            case "prompt.runNewTab": await RunAsync(Prompt.Text, newTab: true); break;
            case "screen.cancel": await CancelAsync(context); break;
            case "screen.save": if (Tabs.Active is { } s) await s.Screen.SaveAsync(); break;
            case "screen.find": Tabs.Active?.Screen.Find(); break;
            case "tab.new":
                nextRunInNewTab = true;
                FocusPromptRequested?.Invoke();
                break;
            case "tab.close": await Tabs.CloseActiveAsync(confirm); break;
            case "tab.next": Tabs.Next(); break;
            case "tab.previous": Tabs.Previous(); break;
            case "nav.back": await Tabs.BackAsync(); break;
            case "nav.forward": await Tabs.ForwardAsync(); break;
            case "palette.open": Palette.Open(); break;
            case "screen.savePdf": await RunAsync("PRNT pdf", newTab: false); break;
            case "record.new" or "record.delete" or "line.add" or "line.remove" or "voucher.bills" or "voucher.cancelVoucher"
                or "report.export" or "report.style":
                if (Tabs.Active?.Screen is not Screens.IScreenCommands screen || !screen.CanRun(command)) return false;
                await screen.RunAsync(command);
                break;
            default: return false;
        }
        return true;
    }

    async Task CancelAsync(IReadOnlySet<string> context)
    {
        if (context.Contains("promptFocus") && (nextRunInNewTab || Prompt.Text.Length > 0))
        {
            nextRunInNewTab = false;
            Prompt.Text = "";
            return;
        }
        if (Tabs.Active is { } tab && !tab.Screen.Cancel()) await Tabs.BackAsync();
    }

    async Task RefreshAsync()
    {
        if (Tabs.Active is not { } tab)
        {
            Status.Message = Text.NothingToRefresh;
            return;
        }
        tab.Changed = false;
        await LoadAsync(tab.Screen);
    }

    /// <summary>
    /// Logs out and leaves nothing of this user on screen: no tabs, no company, none of their own settings. False when
    /// the user keeps unsaved edits.
    /// </summary>
    public async Task<bool> LogOutAsync()
    {
        if (Tabs.Tabs.Any(t => t.Screen.HasUnsavedEdits) && !await confirm.AskAsync(Text.DiscardChanges)) return false;
        Clear();
        lastUser = null;
        await Context.Host.LogoutAsync();
        await Context.Settings.LoadAsync(null);
        await Context.Keys.LoadAsync();
        return true;
    }

    void Clear()
    {
        Tabs.CloseAll();
        Context.CloseCompany();
    }

    /// <summary>After setup or login: the user's layers, then the last company, else CMPY when no tab is open.</summary>
    async Task OnLoggedInAsync()
    {
        // Another person logging in over a lost session must not see the last user's tabs or company.
        var user = Context.Host.Session?.UserName;
        if (lastUser is not null && !string.Equals(lastUser, user, StringComparison.OrdinalIgnoreCase)) Clear();
        lastUser = user;
        try
        {
            await Context.Settings.LoadAsync(Context.Company?.Id);
            await Context.Keys.LoadAsync();
            if (Context.Company is null && !await CompanyOpener.TryReopenAsync(Context) && Tabs.Tabs.Count == 0)
                await RunAsync("CMPY", newTab: true);
        }
        catch (HostException e) { Status.Message = e.Message; }
        ShowLoadErrors();
        OnPropertyChanged(nameof(AcceptsTyping));
    }

    /// <summary>The first problem in this PC's files, and how many more there are and where to see them (spec 7).</summary>
    public void ShowLoadErrors()
    {
        var settings = Context.Settings.LoadErrors;
        var keys = Context.Keys.LoadErrors;
        var all = settings.Concat(keys).ToList();
        if (all.Count == 0) return;
        Status.Message = all.Count == 1 ? all[0]
            : string.Format(CultureInfo.InvariantCulture, settings.Count > 0 ? Text.MoreSettingsErrors : Text.MoreKeyErrors, all[0], all.Count - 1);
    }

    void ShowConnection()
    {
        Status.Connection = Context.Host.State switch
        {
            ConnectionState.Connected => string.Format(CultureInfo.InvariantCulture, Text.ConnectedTo, Context.Host.Address),
            ConnectionState.Reconnecting => Text.HostUnreachable,
            ConnectionState.NeedsLogin when Context.Host.Session is not null => Context.Host.Message ?? Text.LogInAgain,
            _ => "",
        };
        OnPropertyChanged(nameof(AcceptsTyping));
    }

    void ShowWorkingDate() =>
        Status.WorkingDate = string.Format(CultureInfo.InvariantCulture, Text.WorkingDateStatus,
            Formatting.DateText.Format(Context.WorkingDate, Context.Settings.Current.Text("ui.dateFormat")));

    void ShowChanged() => Status.Changed = Tabs.Active?.Changed ?? false;

    /// <summary>Runs on the UI thread when there is one; host events arrive on other threads.</summary>
    void Post(Action action)
    {
        if (ui is null || SynchronizationContext.Current == ui) action();
        else ui.Post(_ => action(), null);
    }

    async Task LoadAsync(IScreen screen)
    {
        // A screen shows its own load failures; one that escapes still reaches the status bar, never a dialog.
        try { await screen.LoadAsync(CancellationToken.None); }
        catch (HostException e) { Status.Message = e.Message; }
    }
}
