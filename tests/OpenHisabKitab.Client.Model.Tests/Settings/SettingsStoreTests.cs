using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Core.Settings;
using OpenHisabKitab.Contracts.V1;
using V1Empty = OpenHisabKitab.Contracts.V1.Empty;

namespace OpenHisabKitab.Client.Model.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    readonly ClientTestHost t = new();
    readonly ClientPaths paths;

    public SettingsStoreTests() => paths = new ClientPaths(t.ClientFolder);

    public void Dispose() => t.Dispose();

    async Task<(SettingsStore Store, HostConnection Connection)> LoggedInStore()
    {
        var c = await t.LoggedInOwnerAsync();
        return (new SettingsStore(new MachineFiles(paths), c), c);
    }

    [Fact]
    public async Task Broken_machine_file_keeps_defaults_and_is_left_alone()
    {
        const string text = "{ \"ui.fontSize\": 99, \"ui.fontFamily\": \"Tahoma\", }";
        File.WriteAllText(paths.Settings, text);
        var store = new SettingsStore(new MachineFiles(paths), t.Connection());
        await store.LoadAsync(company: null);
        Assert.Equal(14, store.Current.Int("ui.fontSize"));
        Assert.Equal("Tahoma", store.Current.Text("ui.fontFamily"));
        Assert.Equal("settings.json line 1: Setting ui.fontSize must be a whole number from 12 to 28.", store.LoadErrors.Single());
        Assert.Equal(text, File.ReadAllText(paths.Settings));
    }

    [Fact]
    public async Task A_form_save_keeps_other_entries_and_never_overwrites_broken_json()
    {
        File.WriteAllText(paths.Settings, "{ \"ui.fontSize\": 99, \"ui.fontFamily\": \"Tahoma\" }");
        var store = new SettingsStore(new MachineFiles(paths), t.Connection());
        await store.LoadAsync(null);
        await store.SetAsync("reports.autoReload", "true", SettingLayer.Machine, null);
        var text = File.ReadAllText(paths.Settings);
        Assert.Contains("99", text, StringComparison.Ordinal);
        Assert.Contains("Tahoma", text, StringComparison.Ordinal);
        Assert.Contains("reports.autoReload", text, StringComparison.Ordinal);

        File.WriteAllText(paths.Settings, "{ \"ui.fontSize\": ");
        await store.LoadAsync(null);
        await store.SetAsync("ui.fontSize", "20", SettingLayer.Machine, null);
        Assert.Equal("{ \"ui.fontSize\": ", File.ReadAllText(paths.Settings));
        Assert.Equal(20, store.Current.Int("ui.fontSize"));
    }

    [Fact]
    public async Task Missing_machine_file_gives_defaults_and_no_errors()
    {
        var store = new SettingsStore(new MachineFiles(paths), t.Connection());
        await store.LoadAsync(null);
        Assert.Empty(store.LoadErrors);
        Assert.Equal(47250, store.Current.Int("connection.port"));
        Assert.Equal("{}", store.LayerText(SettingLayer.Machine));
    }

    [Fact]
    public async Task User_layer_beats_machine_and_follows_the_user()
    {
        var (store, connection) = await LoggedInStore();
        await store.LoadAsync(null);
        await store.SetAsync("ui.fontSize", "16", SettingLayer.Machine, null);
        await store.SetAsync("ui.fontSize", "20", SettingLayer.User, null);
        Assert.Equal(("20", SettingLayer.User), store.Current.Value("ui.fontSize"));
        var other = new SettingsStore(new MachineFiles(new ClientPaths(Path.Combine(t.ClientFolder, "other-pc"))), connection);
        await other.LoadAsync(null);
        Assert.Equal(20, other.Current.Int("ui.fontSize"));
    }

    [Fact]
    public async Task Json_save_with_one_bad_key_writes_nothing()
    {
        var (store, _) = await LoggedInStore();
        await store.LoadAsync(null);
        var errors = await store.SaveLayerTextAsync(SettingLayer.User, "{ \"ui.fontSize\": 18, \"invoice.roundOff\": false }", null);
        Assert.Equal("Setting invoice.roundOff cannot be set for a user.", errors.Single().Message);
        Assert.Equal(14, store.Current.Int("ui.fontSize"));
        errors = await store.SaveLayerTextAsync(SettingLayer.Machine, "{ \"ui.fontSize\": 30 }", null);
        Assert.Single(errors);
        Assert.False(File.Exists(paths.Settings));
    }

    [Fact]
    public async Task Machine_json_is_written_as_typed()
    {
        var store = new SettingsStore(new MachineFiles(paths), t.Connection());
        const string text = "{\n  // bigger on this PC\n  \"ui.fontSize\": 18,\n}";
        Assert.Empty(await store.SaveLayerTextAsync(SettingLayer.Machine, text, null));
        Assert.Equal(text, File.ReadAllText(paths.Settings));
        Assert.Equal(18, store.Current.Int("ui.fontSize"));
        Assert.Equal(text, store.LayerText(SettingLayer.Machine));
    }

    [Fact]
    public async Task Company_layer_comes_from_the_open_company()
    {
        var (store, connection) = await LoggedInStore();
        var company = await t.NewCompanyAsync(connection);
        await store.LoadAsync(company);
        Assert.True(store.Current.Bool("invoice.roundOff"));
        await store.SetAsync("invoice.roundOff", "false", SettingLayer.Company, company);
        Assert.Equal(("false", SettingLayer.Company), store.Current.Value("invoice.roundOff"));
        await store.LoadAsync(null);
        Assert.True(store.Current.Bool("invoice.roundOff"));
    }

    [Fact]
    public async Task Removed_key_in_user_json_is_reset_on_the_host()
    {
        var (store, connection) = await LoggedInStore();
        await store.LoadAsync(null);
        Assert.Empty(await store.SaveLayerTextAsync(SettingLayer.User, "{ \"ui.fontSize\": 18, \"ui.fontFamily\": \"Tahoma\" }", null));
        Assert.Empty(await store.SaveLayerTextAsync(SettingLayer.User, "{ \"ui.fontSize\": 20 }", null));
        var stored = await connection.CallAsync((h, o) => h.UserSettings.ListAsync(new V1Empty(), o));
        Assert.Equal([("ui.fontSize", "20")], stored.Values.Select(v => (v.Key, v.Json)));
        Assert.Equal("Segoe UI", store.Current.Text("ui.fontFamily"));
    }

    [Fact]
    public async Task Reset_removes_the_value_from_its_layer_and_changed_fires()
    {
        var (store, _) = await LoggedInStore();
        await store.LoadAsync(null);
        var changes = 0;
        store.Changed += () => changes++;
        await store.SetAsync("ui.fontSize", "20", SettingLayer.User, null);
        await store.ResetAsync("ui.fontSize", SettingLayer.User, null);
        Assert.Equal(14, store.Current.Int("ui.fontSize"));
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Keybindings_stored_for_the_user_do_not_show_as_settings()
    {
        var (store, connection) = await LoggedInStore();
        await connection.CallAsync((h, o) => h.UserSettings.SetAsync(new SettingValue { Key = "keybindings", Json = "[]" }, o));
        await store.LoadAsync(null);
        Assert.Equal("{}", store.LayerText(SettingLayer.User).Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unreachable_host_keeps_the_remote_layers_last_read()
    {
        var (store, connection) = await LoggedInStore();
        await store.LoadAsync(null);
        await store.SetAsync("ui.fontSize", "20", SettingLayer.User, null);
        t.Offline = true;
        await store.LoadAsync(null);
        Assert.Equal(20, store.Current.Int("ui.fontSize"));
        var fresh = new SettingsStore(new MachineFiles(paths), connection);
        await fresh.LoadAsync(null);
        Assert.Equal(14, fresh.Current.Int("ui.fontSize"));
        await Assert.ThrowsAsync<HostException>(() => fresh.SaveLayerTextAsync(SettingLayer.User, "{ }", null));
    }
}
