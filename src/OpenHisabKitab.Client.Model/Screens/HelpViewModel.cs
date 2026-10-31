using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Screens;

public sealed record FunctionRow(string Code, string Name, string Category, string Key);

public sealed record ShortcutRow(string Key, string Command, string When);

public sealed record FunctionDetail(string Code, string Name, string Purpose, string Key);

/// <summary>HELP: every function and shortcut (spec 6.3). HELP CODE shows one function. Keys shown are the ones in force.</summary>
public sealed partial class HelpViewModel : ScreenViewModel
{
    IReadOnlyList<FunctionRow> allFunctions = [];
    IReadOnlyList<ShortcutRow> allShortcuts = [];
    readonly string? asked;

    HelpViewModel(ShellContext ctx, string? asked) : base(ctx) => this.asked = asked;

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new HelpViewModel(ctx, cmd.Args.FirstOrDefault()?.ToUpperInvariant());

    public override string Code => "HELP";

    public override string Subject => One?.Code ?? "";

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<FunctionRow> Functions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ShortcutRow> Shortcuts { get; private set; } = [];

    [ObservableProperty]
    public partial FunctionDetail? One { get; private set; }

    /// <summary>Raised by Ctrl+F; the view focuses the filter box.</summary>
    public event Action? FocusFilterRequested;

    public override Task LoadAsync(CancellationToken ct)
    {
        var keys = Ctx.Keys.Current;
        allFunctions = [.. FunctionCatalog.All.Select(f => new FunctionRow(f.Code, f.Name, Category(f.Category), KeyOf(keys, "run." + f.Code)))];
        allShortcuts = [.. keys.All.Select(b => new ShortcutRow(KeyText.Display(b.Key), CommandName(b.Command), b.When.ToString()))];
        One = null;
        Message = null;
        if (asked is not null)
        {
            if (FunctionCatalog.Find(asked) is { } f)
                One = new FunctionDetail(f.Code, f.Name, Text.ResourceManager.GetString("Purpose_" + f.Code, Text.Culture)!, KeyOf(keys, "run." + f.Code));
            else Message = string.Format(CultureInfo.InvariantCulture, Text.UnknownFunction, asked);
        }
        Apply();
        return Task.CompletedTask;
    }

    public override void Find() => FocusFilterRequested?.Invoke();

    /// <summary>Esc clears the filter first.</summary>
    public override bool Cancel()
    {
        if (Filter.Length == 0) return false;
        Filter = "";
        return true;
    }

    partial void OnFilterChanged(string value) => Apply();

    void Apply()
    {
        var f = Filter.Trim();
        bool Has(string s) => s.Contains(f, StringComparison.OrdinalIgnoreCase);
        Functions = f.Length == 0 ? allFunctions : [.. allFunctions.Where(r => Has(r.Code) || Has(r.Name))];
        Shortcuts = f.Length == 0 ? allShortcuts : [.. allShortcuts.Where(r => Has(r.Command) || Has(r.Key))];
    }

    static string KeyOf(KeybindingSet keys, string command) => keys.KeyFor(command) is { } chord ? KeyText.Display(chord) : "";

    static string Category(FunctionCategory c) => Text.ResourceManager.GetString("Category_" + c, Text.Culture)!;

    /// <summary>The plain name of a command: "Close tab", "Go to tab 3", or a function's own name for run.CODE.</summary>
    public static string CommandName(string command)
    {
        if (command.StartsWith("run.", StringComparison.Ordinal)) return FunctionCatalog.Find(command[4..])?.Name ?? command;
        if (command.StartsWith("tab.goto.", StringComparison.Ordinal))
            return string.Format(CultureInfo.InvariantCulture, Text.Cmd_tab_goto, command["tab.goto.".Length..]);
        return Text.ResourceManager.GetString("Cmd_" + command.Replace('.', '_'), Text.Culture) ?? command;
    }
}
