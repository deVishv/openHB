using OpenHisabKitab.Client.Model.Theme;

namespace OpenHisabKitab.Client.Model.Tests.Theme;

/// <summary>Spec 6.6: WCAG 2.2 AA. Text at least 4.5:1; control borders and focus indicators at least 3:1.</summary>
public sealed class ContrastTests
{
    static string Get(string name) => (string)typeof(ThemeColors).GetProperty(name)!.GetValue(ThemeColors.Classic)!;

    [Theory]
    [InlineData(nameof(ThemeColors.Text), nameof(ThemeColors.Face), 4.5)]
    [InlineData(nameof(ThemeColors.ContentText), nameof(ThemeColors.Content), 4.5)]
    [InlineData(nameof(ThemeColors.SelectionText), nameof(ThemeColors.Selection), 4.5)]
    [InlineData(nameof(ThemeColors.DisabledText), nameof(ThemeColors.Face), 4.5)]
    [InlineData(nameof(ThemeColors.InactiveTitleText), nameof(ThemeColors.InactiveTitle), 4.5)]
    [InlineData(nameof(ThemeColors.Focus), nameof(ThemeColors.Face), 3.0)]
    [InlineData(nameof(ThemeColors.Focus), nameof(ThemeColors.Content), 3.0)]
    [InlineData(nameof(ThemeColors.FocusOnSelection), nameof(ThemeColors.Selection), 3.0)]
    public void Classic_theme_meets_wcag_aa(string fore, string back, double minimum) =>
        Assert.True(Contrast.Ratio(Get(fore), Get(back)) >= minimum, $"{fore} on {back}: {Contrast.Ratio(Get(fore), Get(back)):F2}");

    [Fact]
    public void Ratio_matches_known_values()
    {
        Assert.Equal(21.0, Contrast.Ratio("#000000", "#FFFFFF"), 1);
        Assert.Equal(21.0, Contrast.Ratio("#FFFFFF", "#000000"), 1);
        Assert.Equal(5.7, Contrast.Ratio("#404040", "#C0C0C0"), 1);   // spec 6.6, disabled text
        Assert.Equal(16.0, Contrast.Ratio("#FFFFFF", "#000080"), 0);  // spec 6.6, selection
        Assert.Equal(1.0, Contrast.Ratio("#808080", "#808080"), 2);
    }

    [Fact]
    public void Spec_palette_is_the_classic_theme()
    {
        var c = ThemeColors.Classic;
        Assert.Equal(("#C0C0C0", "#000000", "#FFFFFF", "#000080", "#FFFFFF"), (c.Face, c.Text, c.Content, c.Selection, c.SelectionText));
        Assert.Equal(("#404040", "#404040", "#FFFFFF"), (c.DisabledText, c.InactiveTitle, c.InactiveTitleText));
    }
}
