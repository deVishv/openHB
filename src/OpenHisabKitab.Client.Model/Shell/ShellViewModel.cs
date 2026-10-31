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
public sealed partial class ShellViewModel : ObservableObject
{
    readonly FunctionRouter router;
    readonly IConfirm confirm;
    bool nextRunInNewTab;

    public ShellViewModel(ShellContext ctx, FunctionRouter router, IConfirm confirm)
    {
        Context = ctx;
        this.router = router;
        this.confirm = confirm;
        Tabs = new TabsViewModel(command => router.Route(command, ctx).Screen, LoadAsync, confirm);
        Palette = new PaletteViewModel(command => RunAsync(command, newTab: false));
        FKeys = FKeyRow.From(ctx.Keys.Current);
        ctx.Keys.Changed += () => FKeys = FKeyRow.From(ctx.Keys.Current);
        ctx.CompanyChanged += () => OnPropertyChanged(nameof(Title));
        ctx.Reported += message => Status.Message = message;
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
        var route = router.Route(input, Context);
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

    async Task LoadAsync(IScreen screen)
    {
        // A screen shows its own load failures; one that escapes still reaches the status bar, never a dialog.
        try { await screen.LoadAsync(CancellationToken.None); }
        catch (HostException e) { Status.Message = e.Message; }
    }
}
