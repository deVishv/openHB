using System.Globalization;

namespace OpenHisabKitab.Client.Model.Theme;

/// <summary>The WCAG 2.2 contrast ratio of two "#RRGGBB" colours, from 1 to 21.</summary>
public static class Contrast
{
    public static double Ratio(string hexA, string hexB)
    {
        var a = Luminance(hexA);
        var b = Luminance(hexB);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    static double Luminance(string hex)
    {
        var rgb = int.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return 0.2126 * Channel(rgb >> 16) + 0.7152 * Channel(rgb >> 8) + 0.0722 * Channel(rgb);
    }

    static double Channel(int value)
    {
        var c = (value & 0xFF) / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
