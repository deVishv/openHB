using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Tests.Commands.Keys;

public sealed class KeybindingSetTests
{
    static readonly HashSet<string> Empty = [];

    static KeyChord Chord(string text)
    {
        Assert.True(KeyChord.TryParse(text, out var c));
        return c;
    }

    static IReadOnlyList<KeybindingEntry> Entries(string json)
    {
        var load = KeybindingFile.Parse(json);
        Assert.Empty(load.Errors);
        return load.Entries;
    }

    [Fact]
    public void Enter_runs_only_in_the_prompt()
    {
        var set = KeybindingSet.Build([], []);
        Assert.Equal("prompt.run", set.Resolve(Chord("enter"), new HashSet<string> { "promptFocus" }));
        Assert.Null(set.Resolve(Chord("enter"), new HashSet<string> { "gridFocus" }));
    }

    [Fact]
    public void Defaults_follow_the_spec_tables()
    {
        var set = KeybindingSet.Build([], []);
        string[] fkeys = ["HELP", "DATE", "FIND", "CMPY", "RFSH", "SALE", "PURC", "RCPT", "PYMT", "JRNL", "PRNT", "SETG"];
        for (var i = 0; i < 12; i++) Assert.Equal("run." + fkeys[i], set.Resolve(Chord($"f{i + 1}"), Empty));
        Assert.Equal("prompt.focus", set.Resolve(Chord("ctrl+l"), Empty));
        Assert.Equal("tab.goto.9", set.Resolve(Chord("ctrl+9"), Empty));
        Assert.Equal("palette.open", set.Resolve(Chord("ctrl+shift+p"), Empty));
        Assert.Equal("tab.previous", set.Resolve(Chord("ctrl+shift+tab"), Empty));
        Assert.Null(set.Resolve(Chord("escape"), new HashSet<string> { "paletteOpen" }));
        Assert.Equal(SettingLayer.Default, set.All[0].From);
    }

    [Fact]
    public void User_rebinds_f6_and_removes_ctrl_l()
    {
        var user = KeybindingFile.Parse("""[ {"key":"f6","command":"run.PURC"}, {"key":"ctrl+l","command":"-prompt.focus"} ]""");
        var set = KeybindingSet.Build([], user.Entries);
        Assert.Equal("run.PURC", set.Resolve(Chord("f6"), Empty));
        Assert.Null(set.Resolve(Chord("ctrl+l"), Empty));
        Assert.Equal(SettingLayer.User, set.All.First(b => b.Key == Chord("f6")).From);
        Assert.Null(set.KeyFor("run.SALE"));
        Assert.Equal(Chord("f6"), set.KeyFor("run.PURC"));
    }

    [Fact]
    public void A_bad_entry_is_skipped_by_line_and_the_rest_apply()
    {
        var load = KeybindingFile.Parse("[\n {\"key\":\"f6\",\"command\":\"run.PURC\"},\n {\"key\":\"f6\",\"command\":\"run.NOPE\"},\n]");
        Assert.Single(load.Entries);
        Assert.Equal(new SettingError(3, "Unknown command: run.NOPE."), load.Errors.Single());
    }

    [Theory]
    [InlineData("""[ {"key":"ctrl+","command":"tab.new"} ]""", "Cannot read key: ctrl+.")]
    [InlineData("""[ {"command":"tab.new"} ]""", "Each entry needs \"key\" and \"command\".")]
    [InlineData("""[ 5 ]""", "Each entry needs \"key\" and \"command\".")]
    [InlineData("""[ {"key":"f6","command":"run.SALE","when":"focus"} ]""", "Unknown when key: focus.")]
    [InlineData("""{ "key": "f6" }""", "This file is not valid JSON.")]
    [InlineData("""[ {"key":"f6" """, "This file is not valid JSON.")]
    public void Bad_files_and_entries_name_the_problem(string json, string message) =>
        Assert.Equal(message, KeybindingFile.Parse(json).Errors.Single().Message);

    [Fact]
    public void Later_entry_in_one_layer_wins()
    {
        var set = KeybindingSet.Build(Entries("""[ {"key":"f6","command":"run.PURC"}, {"key":"f6","command":"run.RCPT"} ]"""), []);
        Assert.Equal("run.RCPT", set.Resolve(Chord("f6"), Empty));
    }

    [Fact]
    public void User_beats_machine()
    {
        var set = KeybindingSet.Build(Entries("""[ {"key":"f6","command":"run.PURC"} ]"""), Entries("""[ {"key":"f6","command":"run.RCPT"} ]"""));
        Assert.Equal("run.RCPT", set.Resolve(Chord("f6"), Empty));
    }

    [Fact]
    public void Removal_with_when_removes_only_that_binding()
    {
        var set = KeybindingSet.Build([], Entries("""[ {"key":"enter","command":"-prompt.run","when":"inputFocus"} ]"""));
        Assert.Equal("prompt.run", set.Resolve(Chord("enter"), new HashSet<string> { "promptFocus" }));
        set = KeybindingSet.Build([], Entries("""[ {"key":"enter","command":"-prompt.run","when":"promptFocus"} ]"""));
        Assert.Null(set.Resolve(Chord("enter"), new HashSet<string> { "promptFocus" }));
    }

    [Fact]
    public void Comments_are_allowed()
    {
        var load = KeybindingFile.Parse("[\n // purchases on f6\n {\"key\":\"f6\",\"command\":\"run.PURC\"},\n]");
        Assert.Empty(load.Errors);
        Assert.Single(load.Entries);
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        var entries = Entries("""[ {"key":"Shift+Ctrl+P","command":"palette.open","when":"!inputFocus"}, {"key":"ctrl+l","command":"-prompt.focus"} ]""");
        var again = KeybindingFile.Parse(KeybindingFile.Write(entries));
        Assert.Empty(again.Errors);
        Assert.Equal(entries.Select(e => (e.Key, e.Command, e.When.ToString(), e.Remove)), again.Entries.Select(e => (e.Key, e.Command, e.When.ToString(), e.Remove)));
    }

    [Fact]
    public void Every_default_command_is_known()
    {
        Assert.All(DefaultKeybindings.All, e => Assert.True(KeyCommands.IsKnown(e.Command), e.Command));
        Assert.Contains("run.TRLB", KeyCommands.All);
        Assert.False(KeyCommands.IsKnown("run.NOPE"));
    }
}
