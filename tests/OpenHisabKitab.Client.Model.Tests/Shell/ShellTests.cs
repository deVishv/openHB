using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Time;
using static OpenHisabKitab.Client.Model.Tests.Shell.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Shell;

public sealed class ShellTests : IDisposable
{
    readonly ClientTestHost t = new();
    readonly List<FakeScreen> made = [];

    public void Dispose() => t.Dispose();

    ShellViewModel Shell(string[] built, FakeConfirm? confirm = null, bool unsaved = false, string? machineKeys = null)
    {
        if (machineKeys is not null) File.WriteAllText(Path.Combine(t.ClientFolder, "keybindings.json"), machineKeys);
        return t.Shell(Fakes(made, unsaved, built), confirm ?? new FakeConfirm(true));
    }

    [Theory]
    [InlineData("xyzq", "Unknown function: XYZQ")]
    [InlineData("12", "Unknown function: 12")]
    [InlineData("xyzq trlb", "Unknown function: XYZQ")]
    [InlineData("trlb", "TRLB is not in this version yet.")]
    [InlineData("TRLB fy", "TRLB is not in this version yet.")]
    public async Task Unbuilt_or_unknown_codes_show_one_line_and_open_nothing(string input, string status)
    {
        var shell = Shell(built: ["HELP"]);
        await shell.RunAsync(input, newTab: false);
        Assert.Equal(status, shell.Status.Message);
        Assert.Empty(shell.Tabs.Tabs);
    }

    [Fact]
    public async Task Enter_replaces_the_active_tab_and_shift_enter_opens_another()
    {
        var shell = Shell(built: ["HELP", "DATE"]);
        await shell.RunAsync("help", false);
        await shell.RunAsync("date", false);
        await shell.RunAsync("help", true);
        Assert.Equal(["1 DATE", "2 HELP"], shell.Tabs.Tabs.Select(t => t.Title));
        shell.Tabs.GoTo(1);
        await shell.Tabs.BackAsync();
        Assert.Equal("1 HELP", shell.Tabs.Active!.Title);
        await shell.Tabs.ForwardAsync();
        Assert.Equal("1 DATE", shell.Tabs.Active!.Title);
    }

    [Fact]
    public async Task A_run_loads_the_screen_clears_the_prompt_and_the_last_message()
    {
        var shell = Shell(built: ["HELP"]);
        await shell.RunAsync("xyzq", false);
        shell.Prompt.Text = "help trlb";
        await shell.HandleKeyAsync(Chord("enter"), InPrompt);
        Assert.Null(shell.Status.Message);
        Assert.Equal("", shell.Prompt.Text);
        Assert.Equal(("1 HELP trlb", 1), (shell.Tabs.Active!.Title, made.Single().Loads));
    }

    [Fact]
    public async Task Closing_unsaved_tab_asks_once_and_no_keeps_it()
    {
        var confirm = new FakeConfirm(answer: false);
        var shell = Shell(built: ["SETG"], confirm: confirm, unsaved: true);
        await shell.RunAsync("setg", false);
        Assert.True(await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext));
        Assert.Equal((1, 1), (confirm.Asked, shell.Tabs.Tabs.Count));
        Assert.Equal("Discard unsaved changes?", confirm.LastQuestion);
    }

    [Fact]
    public async Task Replacing_an_unsaved_screen_asks_first()
    {
        var shell = Shell(built: ["SETG", "HELP"], confirm: new FakeConfirm(false), unsaved: true);
        await shell.RunAsync("setg", false);
        await shell.RunAsync("help", false);
        Assert.Equal("1 SETG", shell.Tabs.Active!.Title);
    }

    [Fact]
    public async Task Removed_binding_is_not_handled_and_rebound_f6_relabels()
    {
        var shell = Shell(built: ["HELP"], machineKeys: """[ {"key":"ctrl+l","command":"-prompt.focus"}, {"key":"f6","command":"run.PURC"} ]""");
        var focused = 0;
        shell.FocusPromptRequested += () => focused++;
        Assert.False(await shell.HandleKeyAsync(Chord("ctrl+l"), NoContext));
        Assert.Equal(0, focused);
        Assert.Equal(new FKeyLabel("F6", "PURC"), shell.FKeys[5]);
        Assert.Equal(new FKeyLabel("F1", "HELP"), shell.FKeys[0]);
        Assert.Equal(12, shell.FKeys.Count);
    }

    [Fact]
    public async Task F1_runs_help_and_ctrl_l_focuses_the_prompt()
    {
        var shell = Shell(built: ["HELP"]);
        var focused = 0;
        shell.FocusPromptRequested += () => focused++;
        Assert.True(await shell.HandleKeyAsync(Chord("f1"), NoContext));
        Assert.True(await shell.HandleKeyAsync(Chord("ctrl+l"), NoContext));
        Assert.Equal(("1 HELP", 1), (shell.Tabs.Active!.Title, focused));
        Assert.False(await shell.HandleKeyAsync(Chord("ctrl+q"), NoContext));
    }

    [Fact]
    public async Task Tab_numbers_close_up_after_a_close()
    {
        var shell = Shell(built: ["HELP", "DATE", "CMPY"]);
        await shell.RunAsync("help", true);
        await shell.RunAsync("date", true);
        await shell.RunAsync("cmpy", true);
        shell.Tabs.GoTo(2);
        await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext);
        Assert.Equal(["1 HELP", "2 CMPY"], shell.Tabs.Tabs.Select(t => t.Title));
        Assert.Equal("2 CMPY", shell.Tabs.Active!.Title);
        await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext);
        await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext);
        Assert.Null(shell.Tabs.Active);
        Assert.False(await shell.Tabs.CloseActiveAsync(new FakeConfirm(true)));
    }

    [Fact]
    public async Task Ctrl_t_then_enter_opens_a_new_tab_and_esc_in_the_prompt_forgets_it()
    {
        var shell = Shell(built: ["HELP", "DATE"]);
        await shell.RunAsync("help", false);
        await shell.HandleKeyAsync(Chord("ctrl+t"), NoContext);
        await shell.RunAsync("date", false);
        Assert.Equal(2, shell.Tabs.Tabs.Count);
        await shell.HandleKeyAsync(Chord("ctrl+t"), NoContext);
        await shell.HandleKeyAsync(Chord("escape"), InPrompt);
        await shell.RunAsync("help", false);
        Assert.Equal(["1 HELP", "2 HELP"], shell.Tabs.Tabs.Select(t => t.Title));
    }

    [Fact]
    public async Task Ctrl_digit_and_ctrl_tab_move_between_tabs()
    {
        var shell = Shell(built: ["HELP", "DATE"]);
        await shell.RunAsync("help", true);
        await shell.RunAsync("date", true);
        await shell.HandleKeyAsync(Chord("ctrl+1"), NoContext);
        Assert.Equal(1, shell.Tabs.Active!.Number);
        await shell.HandleKeyAsync(Chord("ctrl+9"), NoContext);
        Assert.Equal(1, shell.Tabs.Active!.Number);
        await shell.HandleKeyAsync(Chord("ctrl+tab"), NoContext);
        Assert.Equal(2, shell.Tabs.Active!.Number);
        await shell.HandleKeyAsync(Chord("ctrl+tab"), NoContext);
        Assert.Equal(1, shell.Tabs.Active!.Number);
        await shell.HandleKeyAsync(Chord("ctrl+shift+tab"), NoContext);
        Assert.Equal(2, shell.Tabs.Active!.Number);
    }

    [Fact]
    public async Task Esc_goes_back_when_the_screen_does_not_handle_it()
    {
        var shell = Shell(built: ["HELP", "DATE"]);
        await shell.RunAsync("help", false);
        await shell.RunAsync("date", false);
        await shell.HandleKeyAsync(Chord("escape"), NoContext);
        Assert.Equal("1 HELP", shell.Tabs.Active!.Title);
        await shell.HandleKeyAsync(Chord("alt+right"), NoContext);
        Assert.Equal("1 DATE", shell.Tabs.Active!.Title);
        await shell.HandleKeyAsync(Chord("alt+left"), NoContext);
        Assert.Equal("1 HELP", shell.Tabs.Active!.Title);
    }

    [Fact]
    public async Task Ctrl_s_and_ctrl_f_go_to_the_active_screen()
    {
        var shell = Shell(built: ["HELP"]);
        await shell.RunAsync("help", false);
        await shell.HandleKeyAsync(Chord("ctrl+s"), NoContext);
        await shell.HandleKeyAsync(Chord("ctrl+f"), NoContext);
        Assert.Equal((1, 1), (made.Single().Saves, made.Single().Finds));
    }

    [Fact]
    public async Task Rfsh_reloads_the_active_tab_and_clears_changed()
    {
        var shell = Shell(built: ["HELP"]);
        await shell.RunAsync("help", false);
        shell.Tabs.Active!.Changed = true;
        await shell.RunAsync("rfsh", false);
        Assert.Equal((2, false), (made.Single().Loads, shell.Tabs.Active!.Changed));
        await shell.HandleKeyAsync(Chord("f5"), NoContext);
        Assert.Equal(3, made.Single().Loads);
    }

    [Fact]
    public async Task Rfsh_with_no_tab_says_nothing_to_refresh()
    {
        var shell = Shell(built: ["HELP"]);
        await shell.RunAsync("rfsh", false);
        Assert.Equal("Nothing to refresh.", shell.Status.Message);
    }

    [Fact]
    public void Suggestions_follow_typing_and_stop_after_a_space()
    {
        var shell = Shell(built: ["HELP"]);
        Assert.Empty(shell.Prompt.Suggestions);
        shell.Prompt.Text = "tr";
        Assert.Equal("TRLB", shell.Prompt.Suggestions[0].Code);
        shell.Prompt.Text = "bal";
        Assert.Equal(["TRLB", "BSHT"], shell.Prompt.Suggestions.Select(f => f.Code));
        shell.Prompt.Text = "trlb ";
        Assert.Empty(shell.Prompt.Suggestions);
    }

    [Fact]
    public void Complete_puts_the_selected_code()
    {
        var shell = Shell(built: ["HELP"]);
        shell.Prompt.Text = "bal";
        shell.Prompt.SelectedSuggestion = 1;
        shell.Prompt.Complete();
        Assert.Equal("BSHT ", shell.Prompt.Text);
        shell.Prompt.SelectedSuggestion = -1;
        shell.Prompt.Complete();
        Assert.Equal("BSHT ", shell.Prompt.Text);
    }

    [Fact]
    public async Task Palette_finds_settings_and_opens_setg_for_one()
    {
        var shell = Shell(built: ["SETG"]);
        await shell.HandleKeyAsync(Chord("ctrl+shift+p"), NoContext);
        Assert.True(shell.Palette.IsOpen);
        shell.Palette.Text = "font";
        Assert.Equal(["ui.fontFamily  Font for screen text.", "ui.fontSize  Text size in pixels."], shell.Palette.Items.Select(i => i.Label));
        shell.Palette.Selected = 1;
        await shell.Palette.AcceptAsync();
        Assert.False(shell.Palette.IsOpen);
        Assert.Equal("1 SETG ui.fontSize", shell.Tabs.Active!.Title);
    }

    [Fact]
    public async Task Palette_runs_a_function_and_esc_closes_it()
    {
        var shell = Shell(built: ["HELP"]);
        shell.Palette.Open();
        shell.Palette.Text = "help";
        Assert.Equal("HELP  Help", shell.Palette.Items[0].Label);
        await shell.Palette.AcceptAsync();
        Assert.Equal("1 HELP", shell.Tabs.Active!.Title);
        shell.Palette.Open();
        Assert.False(await shell.HandleKeyAsync(Chord("escape"), new HashSet<string> { "paletteOpen" }));
        Assert.Equal("1 HELP", shell.Tabs.Active!.Title);   // Esc in the palette is the palette's, not Back
        shell.Palette.Close();
        Assert.False(shell.Palette.IsOpen);
    }

    [Fact]
    public void Title_follows_the_open_company()
    {
        var shell = Shell(built: ["HELP"]);
        Assert.Equal("Open Hisab Kitab", shell.Title);
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        shell.Context.OpenCompany(new CompanyEntry { Id = 1, Name = "A Traders" }, new FinancialYear(2026));
        Assert.Equal("Open Hisab Kitab: A Traders FY 2026-27", shell.Title);
        Assert.Contains(nameof(ShellViewModel.Title), changed);
    }
}
