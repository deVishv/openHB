using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Tests.Settings;

public sealed class SettingsDocumentTests
{
    [Fact]
    public void Comments_and_trailing_commas_are_accepted_and_bad_entries_are_skipped_by_line()
    {
        var load = SettingsDocument.Parse("{\n // mine\n \"ui.fontSize\": 16,\n \"ui.dateFormat\": \"mm-dd\",\n}", SettingLayer.Machine);
        Assert.Equal("16", load.Values["ui.fontSize"]);
        Assert.False(load.Values.ContainsKey("ui.dateFormat"));
        Assert.Equal(new SettingError(4, "Setting ui.dateFormat must be one of: dd-mm-yyyy, dd/mm/yyyy, yyyy-mm-dd."), load.Errors.Single());
    }

    [Theory]
    [InlineData("{ \"ui.fontSize\": ")]
    [InlineData("[1, 2]")]
    [InlineData("")]
    public void Broken_json_gives_one_error_and_no_values(string text)
    {
        var load = SettingsDocument.Parse(text, SettingLayer.Machine);
        Assert.Empty(load.Values);
        Assert.Equal("This file is not valid JSON.", load.Errors.Single().Message);
    }

    [Fact]
    public void Broken_json_error_carries_its_line()
    {
        var load = SettingsDocument.Parse("{\n \"ui.fontSize\": 16\n \"ui.fontFamily\": \"Tahoma\"\n}", SettingLayer.Machine);
        Assert.Equal(3, load.Errors.Single().Line);
    }

    [Fact]
    public void Write_then_parse_round_trips()
    {
        var values = new Dictionary<string, string> { ["ui.fontSize"] = "20", ["reports.autoReload"] = "true", ["ui.fontFamily"] = "\"Tahoma\"" };
        var text = SettingsDocument.Write(values);
        Assert.True(text.IndexOf("reports.autoReload", StringComparison.Ordinal) < text.IndexOf("ui.fontFamily", StringComparison.Ordinal));
        var load = SettingsDocument.Parse(text, SettingLayer.User);
        Assert.Empty(load.Errors);
        Assert.Equal(values.OrderBy(p => p.Key, StringComparer.Ordinal), load.Values.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void Wrong_layer_entry_is_skipped()
    {
        var load = SettingsDocument.Parse("{ \"invoice.roundOff\": false, \"ui.fontSize\": 18 }", SettingLayer.User);
        Assert.Equal(["ui.fontSize"], load.Values.Keys);
        Assert.Equal(new SettingError(1, "Setting invoice.roundOff cannot be set for a user."), load.Errors.Single());
    }
}
