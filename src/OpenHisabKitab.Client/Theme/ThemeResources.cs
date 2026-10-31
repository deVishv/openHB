using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OpenHisabKitab.Client.Model.Theme;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Theme;

/// <summary>
/// Puts the theme's brushes and fonts in the application resources (spec 6.6). Every brush comes from ThemeColors, the
/// values the contrast tests check. In Windows High Contrast mode the system colours are used instead.
/// </summary>
public static class ThemeResources
{
    public static void Apply(ResourceDictionary target, ThemeColors colors, EffectiveSettings settings, bool highContrast)
    {
        if (highContrast)
        {
            target["FaceBrush"] = SystemColors.ControlBrush;
            target["TextBrush"] = SystemColors.ControlTextBrush;
            target["ContentBrush"] = SystemColors.WindowBrush;
            target["ContentTextBrush"] = SystemColors.WindowTextBrush;
            target["SelectionBrush"] = SystemColors.HighlightBrush;
            target["SelectionTextBrush"] = SystemColors.HighlightTextBrush;
            target["DisabledTextBrush"] = SystemColors.GrayTextBrush;
            target["InactiveTitleBrush"] = SystemColors.InactiveCaptionBrush;
            target["InactiveTitleTextBrush"] = SystemColors.InactiveCaptionTextBrush;
            target["FocusBrush"] = SystemColors.WindowTextBrush;
            target["FocusOnSelectionBrush"] = SystemColors.HighlightTextBrush;
            target["LightBrush"] = SystemColors.ControlLightLightBrush;
            target["ShadowBrush"] = SystemColors.ControlDarkBrush;
            target["DarkShadowBrush"] = SystemColors.ControlDarkDarkBrush;
        }
        else
        {
            target["FaceBrush"] = Brush(colors.Face);
            target["TextBrush"] = Brush(colors.Text);
            target["ContentBrush"] = Brush(colors.Content);
            target["ContentTextBrush"] = Brush(colors.ContentText);
            target["SelectionBrush"] = Brush(colors.Selection);
            target["SelectionTextBrush"] = Brush(colors.SelectionText);
            target["DisabledTextBrush"] = Brush(colors.DisabledText);
            target["InactiveTitleBrush"] = Brush(colors.InactiveTitle);
            target["InactiveTitleTextBrush"] = Brush(colors.InactiveTitleText);
            target["FocusBrush"] = Brush(colors.Focus);
            target["FocusOnSelectionBrush"] = Brush(colors.FocusOnSelection);
            // The bevel: the outer dark edge (black) is what gives control borders their 3:1 against the face.
            target["LightBrush"] = Brush("#FFFFFF");
            target["ShadowBrush"] = Brush("#808080");
            target["DarkShadowBrush"] = Brush("#000000");
        }
        target["UiFont"] = new FontFamily(settings.Text("ui.fontFamily"));
        target["UiFontSize"] = Math.Round(settings.Int("ui.fontSize") * TextScale.Percent() / 100.0);
        target["NumberFont"] = new FontFamily("Consolas");
    }

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>Windows "Make text bigger" (Settings, Accessibility, Text size): 100 to 225 percent (spec 6.6).</summary>
public static class TextScale
{
    public static int Percent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
        return key?.GetValue("TextScaleFactor") is int percent && percent is >= 100 and <= 225 ? percent : 100;
    }
}
