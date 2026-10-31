using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Tests.Settings;

public sealed class KeybindingStoreTests : IDisposable
{
    static readonly HashSet<string> NoContext = [];

    readonly ClientTestHost t = new();
    readonly ClientPaths paths;

    public KeybindingStoreTests() => paths = new ClientPaths(t.ClientFolder);

    public void Dispose() => t.Dispose();

    static KeyChord Chord(string text) => KeyChord.TryParse(text, out var c) ? c : throw new ArgumentException(text);

    [Fact]
    public async Task User_keybindings_load_from_the_host_and_win()
    {
        var c = await t.LoggedInOwnerAsync();
        File.WriteAllText(paths.Keybindings, """[ {"key":"f6","command":"run.PURC"} ]""");
        var store = new KeybindingStore(new MachineFiles(paths), c);
        Assert.Empty(await store.SaveLayerTextAsync(SettingLayer.User, """[ {"key":"f6","command":"run.RCPT"} ]"""));
        var again = new KeybindingStore(new MachineFiles(paths), c);
        await again.LoadAsync();
        Assert.Equal("run.RCPT", again.Current.Resolve(Chord("f6"), NoContext));
        Assert.Equal("""[ {"key":"f6","command":"run.RCPT"} ]""", again.LayerText(SettingLayer.User));
    }

    [Fact]
    public async Task Broken_keybindings_file_reports_its_line_and_keeps_good_entries()
    {
        const string text = "[\n {\"key\":\"f6\",\"command\":\"run.PURC\"},\n {\"key\":\"f7\",\"command\":\"run.NOPE\"}\n]";
        File.WriteAllText(paths.Keybindings, text);
        var store = new KeybindingStore(new MachineFiles(paths), t.Connection());
        await store.LoadAsync();
        Assert.Equal("keybindings.json line 3: Unknown command: run.NOPE.", store.LoadErrors.Single());
        Assert.Equal("run.PURC", store.Current.Resolve(Chord("f6"), NoContext));
        Assert.Equal(text, File.ReadAllText(paths.Keybindings));
    }

    [Fact]
    public async Task Bad_json_save_writes_nothing()
    {
        var store = new KeybindingStore(new MachineFiles(paths), t.Connection());
        var errors = await store.SaveLayerTextAsync(SettingLayer.Machine, """[ {"key":"ctrl+","command":"tab.new"} ]""");
        Assert.Equal("Cannot read key: ctrl+.", errors.Single().Message);
        Assert.False(File.Exists(paths.Keybindings));
    }

    [Fact]
    public async Task Set_entries_writes_the_layer_and_changed_fires()
    {
        var store = new KeybindingStore(new MachineFiles(paths), t.Connection());
        var changes = 0;
        store.Changed += () => changes++;
        await store.SetEntriesAsync(SettingLayer.Machine, [new KeybindingEntry(Chord("f6"), "run.PURC", WhenClause.Always, false)]);
        Assert.Equal("run.PURC", store.Current.Resolve(Chord("f6"), NoContext));
        Assert.Equal(1, changes);
        Assert.Contains("run.PURC", File.ReadAllText(paths.Keybindings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_files_give_defaults()
    {
        var store = new KeybindingStore(new MachineFiles(paths), t.Connection());
        await store.LoadAsync();
        Assert.Empty(store.LoadErrors);
        Assert.Equal("run.SALE", store.Current.Resolve(Chord("f6"), NoContext));
        Assert.Equal("[]", store.LayerText(SettingLayer.Machine));
        Assert.Equal("[]", store.LayerText(SettingLayer.User));
    }
}
