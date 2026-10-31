using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Settings;
using static OpenHisabKitab.Client.Model.Tests.Shell.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class KeysScreenTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    async Task<(ShellViewModel Shell, KeysViewModel Keys)> OpenKeys()
    {
        var shell = await t.LoggedInShellAsync(open: [], built: ScreenCatalog.Built());
        await shell.RunAsync("KEYS", newTab: true);
        return (shell, (KeysViewModel)shell.Tabs.Active!.Screen);
    }

    static KeyRow Row(KeysViewModel k, string command) => k.Rows.First(r => r.Command == command);

    [Fact]
    public async Task Rows_show_every_binding_with_its_name_and_source()
    {
        var (_, keys) = await OpenKeys();
        var help = Row(keys, "run.HELP");
        Assert.Equal(("F1", "Help", "", SettingLayer.Default), (help.Key, help.CommandName, help.When, help.From));
        Assert.Equal(SettingLayer.User, keys.TargetLayer);
        keys.Filter = "close";
        Assert.Equal(["tab.close"], keys.Rows.Select(r => r.Command));
    }

    [Fact]
    public async Task Recording_f6_as_run_purc_relabels_the_f_key_row()
    {
        var (shell, keys) = await OpenKeys();
        keys.Selected = Row(keys, "run.PURC");
        keys.StartRecording();
        Assert.True(keys.Recording);
        Assert.True(await shell.HandleKeyAsync(Chord("f6"), NoContext));
        Assert.False(keys.Recording);
        Assert.Equal(new FKeyLabel("F6", "PURC"), shell.FKeys[5]);
        Assert.Equal(new FKeyLabel("F7", null), shell.FKeys[6]);
        Assert.Equal(("F6", SettingLayer.User), (Row(keys, "run.PURC").Key, Row(keys, "run.PURC").From));
        Assert.Single(shell.Tabs.Tabs);   // F6 was recorded, not run
    }

    [Fact]
    public async Task An_unbound_key_can_be_recorded()
    {
        var (shell, keys) = await OpenKeys();
        keys.Selected = Row(keys, "run.HELP");
        keys.StartRecording();
        Assert.True(await shell.HandleKeyAsync(Chord("ctrl+q"), NoContext));
        Assert.Equal("Ctrl+Q", Row(keys, "run.HELP").Key);
    }

    [Fact]
    public async Task Esc_cancels_recording()
    {
        var (shell, keys) = await OpenKeys();
        keys.Selected = Row(keys, "run.PURC");
        keys.StartRecording();
        Assert.True(await shell.HandleKeyAsync(Chord("escape"), NoContext));
        Assert.False(keys.Recording);
        Assert.Equal("F7", Row(keys, "run.PURC").Key);
        Assert.Single(shell.Tabs.Tabs);
    }

    [Fact]
    public async Task Reset_restores_the_default_key()
    {
        var (shell, keys) = await OpenKeys();
        keys.Selected = Row(keys, "run.PURC");
        keys.StartRecording();
        await shell.HandleKeyAsync(Chord("f6"), NoContext);
        await keys.ResetAsync(Row(keys, "run.PURC"));
        Assert.Equal(("F7", SettingLayer.Default), (Row(keys, "run.PURC").Key, Row(keys, "run.PURC").From));
        Assert.Equal(new FKeyLabel("F6", "SALE"), shell.FKeys[5]);
    }

    [Fact]
    public async Task Keys_json_with_bad_chord_saves_nothing()
    {
        var (shell, keys) = await OpenKeys();
        keys.JsonMode = true;
        Assert.Equal("[]", keys.JsonText);
        keys.JsonText = "[\n {\"key\":\"ctrl+\",\"command\":\"tab.new\"}\n]";
        await keys.SaveAsync();
        Assert.Equal(["line 2: Cannot read key: ctrl+."], keys.JsonErrors);
        keys.JsonText = """[ {"key":"f6","command":"run.RCPT"} ]""";
        await keys.SaveAsync();
        Assert.Empty(keys.JsonErrors);
        Assert.Equal(new FKeyLabel("F6", "RCPT"), shell.FKeys[5]);
    }

    [Fact]
    public async Task Machine_layer_can_be_the_target()
    {
        var (shell, keys) = await OpenKeys();
        keys.TargetLayer = SettingLayer.Machine;
        keys.Selected = Row(keys, "run.PURC");
        keys.StartRecording();
        await shell.HandleKeyAsync(Chord("f6"), NoContext);
        Assert.Equal(SettingLayer.Machine, Row(keys, "run.PURC").From);
        Assert.Contains("run.PURC", File.ReadAllText(Path.Combine(t.ClientFolder, "keybindings.json")), StringComparison.Ordinal);
    }
}
