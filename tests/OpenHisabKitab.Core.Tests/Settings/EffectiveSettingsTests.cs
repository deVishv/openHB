using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Tests.Settings;

public sealed class EffectiveSettingsTests
{
    static EffectiveSettings Of(params (SettingLayer Layer, (string Key, string Json)[] Values)[] layers) =>
        new(layers.ToDictionary(l => l.Layer, l => (IReadOnlyDictionary<string, string>)l.Values.ToDictionary(v => v.Key, v => v.Json)));

    [Fact]
    public void Later_layers_win_and_modified_means_differs_from_default()
    {
        var s = Of((SettingLayer.Machine, [("ui.fontSize", "16"), ("ui.fontFamily", "\"Segoe UI\"")]),
                   (SettingLayer.User, [("ui.fontSize", "20")]));
        Assert.Equal(("20", SettingLayer.User), s.Value("ui.fontSize"));
        Assert.False(s.IsModified("ui.fontFamily"));
        Assert.True(s.IsModified("ui.fontSize"));
        Assert.Equal(SettingLayer.Default, s.Value("reports.autoReload").From);
    }

    [Fact]
    public void Typed_reads_use_the_effective_value()
    {
        var s = Of((SettingLayer.Company, [("invoice.roundOff", "false")]), (SettingLayer.User, [("ui.fontFamily", "\"Tahoma\"")]));
        Assert.False(s.Bool("invoice.roundOff"));
        Assert.Equal("Tahoma", s.Text("ui.fontFamily"));
        Assert.Equal(14, s.Int("ui.fontSize"));
    }

    [Fact]
    public void A_value_invalid_for_its_layer_falls_back()
    {
        var s = Of((SettingLayer.Machine, [("ui.fontSize", "16")]), (SettingLayer.User, [("ui.fontSize", "99"), ("invoice.roundOff", "false")]));
        Assert.Equal(("16", SettingLayer.Machine), s.Value("ui.fontSize"));
        Assert.True(s.Bool("invoice.roundOff"));
    }

    [Fact]
    public void Unknown_key_throws() =>
        Assert.Throws<ArgumentException>(() => Of().Value("nope"));
}
