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

    public ShellViewModel(ShellContext ctx, FunctionRouter router, IConfirm confirm)
    {
        Context = ctx;
        this.router = router;
        this.confirm = confirm;
        Tabs = new TabsViewModel(async command => (await router.RouteAsync(command, ctx)).Screen, LoadAsync, confirm);
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

    public async Task RunAsync(string input, bool newTab)
    {
        Status.Message = null;
        var route = await router.RouteAsync(input, Context);
        var inNewTab = newTab || nextRunInNewTab;
        nextRunInNewTab = false;
        Prompt.Text = "";
        if (route.Status is { } status) Status.Message = status;
        if (route.Refresh)
        {
            await RefreshAsync();
            return;
        }
        if (route.Screen is not { } screen) return;
        if (!inNewTab && Tabs.Active is { Screen.HasUnsavedEdits: true } && !await confirm.AskAsync(Text.DiscardChanges)) return;
        Tabs.Show(screen, route.Command!, inNewTab);
        await LoadAsync(screen);
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
        if (command is null) return false;
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

    /// <summary>After setup or login: the user's layers, then the last company, else CMPY when no tab is open.</summary>
    async Task OnLoggedInAsync()
    {
        try
        {
            await Context.Settings.LoadAsync(Context.Company?.Id);
            await Context.Keys.LoadAsync();
            if (Context.Company is null && !await CompanyOpener.TryReopenAsync(Context) && Tabs.Tabs.Count == 0)
                await RunAsync("CMPY", newTab: true);
        }
        catch (HostException e) { Status.Message = e.Message; }
        if (Context.Settings.LoadErrors.Concat(Context.Keys.LoadErrors).FirstOrDefault() is { } error) Status.Message = error;
        OnPropertyChanged(nameof(AcceptsTyping));
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
