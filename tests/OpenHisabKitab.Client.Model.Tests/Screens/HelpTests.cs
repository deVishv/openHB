using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class HelpTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    HelpViewModel Help(string input)
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var screen = (HelpViewModel)HelpViewModel.Create(cmd, t.Shell().Context)!;
        screen.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        return screen;
    }

    [Fact]
    public void Help_lists_every_catalog_function_with_f1_on_help()
    {
        var help = Help("HELP");
        Assert.Equal(FunctionCatalog.All.Select(f => f.Code), help.Functions.Select(r => r.Code));
        Assert.Equal(("Help", "System", "F1"), help.Functions.Select(r => (r.Name, r.Category, r.Key)).First());
        Assert.Equal("", help.Functions.Single(r => r.Code == "TRLB").Key);
        Assert.Equal("GST", help.Functions.Single(r => r.Code == "GST1").Category);
        Assert.Contains(help.Shortcuts, s => (s.Key, s.Command) == ("Ctrl+L", "Focus the prompt"));
        Assert.Contains(help.Shortcuts, s => (s.Key, s.Command, s.When) == ("Enter", "Run in this tab", "promptFocus"));
        Assert.Contains(help.Shortcuts, s => (s.Key, s.Command) == ("Ctrl+3", "Go to tab 3"));
        Assert.Contains(help.Shortcuts, s => (s.Key, s.Command) == ("F6", "Sales invoice"));
        Assert.Null(help.One);
        Assert.Null(help.Message);
    }

    [Fact]
    public void Help_code_shows_one_function_with_its_purpose()
    {
        var help = Help("help bsht");
        Assert.Equal(("BSHT", "Balance sheet", ""), (help.One!.Code, help.One.Name, help.One.Key));
        Assert.Equal("Follows the Schedule III layout, with an option to show it grouped by the chart of accounts instead.", help.One.Purpose);
        Assert.Equal("BSHT", help.Subject);
        Assert.Equal("F1", Help("HELP HELP").One!.Key);
    }

    [Fact]
    public void Every_function_has_a_purpose() =>
        Assert.All(FunctionCatalog.All, f => Assert.False(string.IsNullOrWhiteSpace(Help("HELP " + f.Code).One!.Purpose), f.Code));

    [Fact]
    public void Help_unknown_code_shows_the_list_and_one_line()
    {
        var help = Help("HELP XYZQ");
        Assert.Null(help.One);
        Assert.Equal(FunctionCatalog.All.Count, help.Functions.Count);
        Assert.Equal("Unknown function: XYZQ", help.Message);
    }

    [Fact]
    public void Help_filter_matches_codes_names_and_command_names()
    {
        var help = Help("HELP");
        help.Filter = "tab";
        Assert.Contains(help.Shortcuts, s => s.Command == "Close tab");
        Assert.DoesNotContain(help.Shortcuts, s => s.Command == "Save");
        help.Filter = "balance";
        Assert.Equal(["TRLB", "BSHT"], help.Functions.Select(f => f.Code));
        help.Filter = "ledg";
        Assert.Equal(["LEDG"], help.Functions.Select(f => f.Code));
        help.Filter = "";
        Assert.Equal(FunctionCatalog.All.Count, help.Functions.Count);
    }

    [Fact]
    public void Find_asks_the_view_to_focus_the_filter()
    {
        var help = Help("HELP");
        var asked = 0;
        help.FocusFilterRequested += () => asked++;
        help.Find();
        Assert.Equal(1, asked);
    }

    [Fact]
    public void A_rebound_key_shows_in_help()
    {
        File.WriteAllText(Path.Combine(t.ClientFolder, "keybindings.json"), """[ {"key":"f6","command":"run.PURC"} ]""");
        var help = Help("HELP");
        Assert.Equal("F6", help.Functions.Single(f => f.Code == "PURC").Key);
        Assert.Equal("", help.Functions.Single(f => f.Code == "SALE").Key);
    }
}
