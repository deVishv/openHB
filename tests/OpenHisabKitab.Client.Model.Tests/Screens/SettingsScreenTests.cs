using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class SettingsScreenTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<SettingsViewModel> Setg(ShellContext ctx, string input = "SETG")
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var screen = (SettingsViewModel)SettingsViewModel.Create(cmd, ctx)!;
        await screen.LoadAsync(CancellationToken.None);
        return screen;
    }

    static SettingRow Row(SettingsViewModel s, string key) => s.Rows.Single(r => r.Key == key);

    [Fact]
    public async Task Rows_cover_the_catalog_grouped_by_category()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        Assert.Equal(SettingsCatalog.All.Select(d => d.Key).Order(StringComparer.Ordinal), s.Rows.Select(r => r.Key).Order(StringComparer.Ordinal));
        Assert.Equal(s.Rows.Select(r => r.Category).Distinct(), s.Rows.GroupBy(r => r.Category).Select(g => g.Key));
        var size = Row(s, "ui.fontSize");
        Assert.Equal(("Text size in pixels.", "14", SettingLayer.Default), (size.Description, size.ValueText, size.From));
        Assert.Equal("Segoe UI", Row(s, "ui.fontFamily").ValueText);
    }

    [Fact]
    public async Task Modified_setting_is_marked()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        Assert.Equal("ui.fontSize", Row(s, "ui.fontSize").Display);
        await s.SetValueAsync(Row(s, "ui.fontSize"), "20");
        var row = Row(s, "ui.fontSize");
        Assert.Equal(("*ui.fontSize", true, SettingLayer.User, "Saved."), (row.Display, row.Modified, row.From, s.Message));
        await s.ResetAsync(Row(s, "ui.fontSize"));
        Assert.Equal("ui.fontSize", Row(s, "ui.fontSize").Display);
    }

    [Fact]
    public async Task User_target_is_default_for_ui_settings_and_machine_can_be_chosen()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        var row = Row(s, "ui.fontSize");
        Assert.Equal([SettingLayer.Machine, SettingLayer.User], row.Layers);
        Assert.Equal(SettingLayer.User, row.TargetLayer);
        row.TargetLayer = SettingLayer.Machine;
        await s.SetValueAsync(row, "18");
        Assert.Equal((SettingLayer.Machine, 18), (Row(s, "ui.fontSize").From, shell.Context.Settings.Current.Int("ui.fontSize")));
    }

    [Fact]
    public async Task A_bad_value_names_the_problem_and_saves_nothing()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        await s.SetValueAsync(Row(s, "ui.fontSize"), "40");
        Assert.Equal("Setting ui.fontSize must be a whole number from 12 to 28.", s.Message);
        await s.SetValueAsync(Row(s, "ui.fontSize"), "big");
        Assert.Equal("Setting ui.fontSize must be a whole number from 12 to 28.", s.Message);
        Assert.Equal(14, shell.Context.Settings.Current.Int("ui.fontSize"));
    }

    [Fact]
    public async Task Company_layer_needs_manage_companies()
    {
        var owner = await t.LoggedInShellAsync(open: [], company: "A Traders");
        var ownerSetg = await Setg(owner.Context);
        Assert.Equal([SettingLayer.Company], Row(ownerSetg, "invoice.roundOff").Layers);
        var clerk = await t.UserShellAsync("clerk", "DataEntry", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(clerk.Context, owner.Context.Company);
        var s = await Setg(clerk.Context);
        var row = Row(s, "invoice.roundOff");
        Assert.Empty(row.Layers);
        Assert.False(row.CanSet);
        await s.SetValueAsync(row, "false");
        Assert.True(clerk.Context.Settings.Current.Bool("invoice.roundOff"));
    }

    [Fact]
    public async Task No_company_open_means_no_company_layer()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        Assert.Empty(Row(s, "invoice.roundOff").Layers);
    }

    [Fact]
    public async Task Setg_with_a_key_filters_to_it()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context, "SETG ui.fontSize");
        Assert.Equal("ui.fontSize", s.Filter);
        Assert.Equal(["ui.fontSize"], s.Rows.Select(r => r.Key));
        s.Filter = "font";
        Assert.Equal(["ui.fontFamily", "ui.fontSize"], s.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task Json_with_a_bad_key_saves_nothing_and_lists_the_line()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        s.JsonMode = true;
        Assert.Equal(SettingLayer.User, s.JsonLayer);
        Assert.False(s.HasUnsavedEdits);
        s.JsonText = "{\n  \"ui.fontSize\": 18,\n  \"invoice.roundOff\": false\n}";
        Assert.True(s.HasUnsavedEdits);
        await s.SaveAsync();
        Assert.Equal(["line 3: Setting invoice.roundOff cannot be set for a user."], s.JsonErrors);
        Assert.Equal(14, shell.Context.Settings.Current.Int("ui.fontSize"));
        s.JsonText = "{ \"ui.fontSize\": 18 }";
        await s.SaveAsync();
        Assert.Empty(s.JsonErrors);
        Assert.Equal((18, "Saved."), (shell.Context.Settings.Current.Int("ui.fontSize"), s.Message));
        Assert.False(s.HasUnsavedEdits);
    }

    [Fact]
    public async Task Json_layer_switch_shows_that_layer()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        s.JsonMode = true;
        s.JsonLayer = SettingLayer.Machine;
        Assert.Contains("connection.host", s.JsonText, StringComparison.Ordinal);
        Assert.Equal([SettingLayer.Machine, SettingLayer.User], s.JsonLayers);
    }

    [Fact]
    public async Task Host_setting_change_says_restart_when_the_host_says_so()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        Assert.Contains(s.HostRows, r => (r.Key, r.Value) == ("api.enabled", "false"));
        await s.SetHostAsync(s.HostRows.Single(r => r.Key == "api.enabled"), "true");
        Assert.Equal("Saved. Restart the host service to apply.", s.Message);
        Assert.Equal("true", s.HostRows.Single(r => r.Key == "api.enabled").Value);
        await s.SetHostAsync(s.HostRows.Single(r => r.Key == "mcp.allowWrites"), "true");
        Assert.Equal("Saved.", s.Message);
        await s.SetHostAsync(s.HostRows.Single(r => r.Key == "api.port"), "80");
        Assert.Equal("api.port must be 1024 to 65535, not the gRPC port.", s.Message);
    }

    [Fact]
    public async Task Viewer_sees_no_host_rows()
    {
        var owner = await t.LoggedInShellAsync(open: [], company: "A Traders");
        var viewer = await t.UserShellAsync("viewer", "Viewer", owner.Context.Company!.Id);
        var s = await Setg(viewer.Context);
        Assert.False(s.CanSeeHost);
        Assert.Empty(s.HostRows);
    }

    [Fact]
    public async Task Selection_and_target_survive_a_change_from_elsewhere()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        s.Selected = Row(s, "ui.fontSize");
        s.Selected.TargetLayer = SettingLayer.Machine;
        await shell.Context.Settings.SetAsync("reports.autoReload", "true", SettingLayer.User, null);
        Assert.Equal(("ui.fontSize", SettingLayer.Machine), (s.Selected!.Key, s.Selected.TargetLayer));
    }

    [Fact]
    public async Task Json_editor_lists_the_machine_file_errors_when_it_opens()
    {
        File.WriteAllText(Path.Combine(t.ClientFolder, "settings.json"), "{ \"ui.fontSize\": 99, \"ui.zoom\": 1 }");
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        s.JsonMode = true;
        s.JsonLayer = SettingLayer.Machine;
        Assert.Equal(2, s.JsonErrors.Count);
        Assert.StartsWith("line ", s.JsonErrors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connection_change_applies_at_the_next_connect()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var s = await Setg(shell.Context);
        await s.SetValueAsync(Row(s, "connection.port"), "47260");
        Assert.Equal("Saved. Applies at the next connect.", s.Message);
        Assert.Equal(ConnectionState.Connected, shell.Context.Host.State);
    }
}
