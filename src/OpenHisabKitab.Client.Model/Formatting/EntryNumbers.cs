using System.Globalization;

namespace OpenHisabKitab.Client.Model.Formatting;

/// <summary>
/// Rates and quantities as typed in entry screens. Amounts use Core's Money.TryParse. Nothing is rounded: more decimals than
/// the stored unit holds is refused, so what is saved is exactly what was typed.
/// </summary>
public static class EntryNumbers
{
    /// <summary>A GST or cess rate such as "18", "0.25", or "18%", as basis points (18% is 1800). Empty is 0.</summary>
    public static bool TryRate(string? text, out int basisPoints)
    {
        basisPoints = 0;
        var s = (text ?? "").Trim().TrimEnd('%').Trim();
        if (s.Length == 0) return true;
        if (!TryScaled(s, 2, out var value) || value > 100_00) return false;
        basisPoints = (int)value;
        return true;
    }

    /// <summary>A quantity with up to 3 decimals, as thousandths. Commas between digits are allowed.</summary>
    public static bool TryQuantity(string? text, out long thousandths) => TryScaled((text ?? "").Trim().Replace(",", "", StringComparison.Ordinal), 3, out thousandths);

    /// <summary>A whole number of days, 0 to 3650. Empty is 0.</summary>
    public static bool TryDays(string? text, out int days)
    {
        days = 0;
        var s = (text ?? "").Trim();
        return s.Length == 0 || int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out days) && days <= 3650;
    }

    public static string Rate(int basisPoints) => (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    public static string Quantity(long thousandths) => (thousandths / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    // A non negative decimal with at most `places` decimals, scaled to a whole number.
    static bool TryScaled(string s, int places, out long scaled)
    {
        scaled = 0;
        if (s.Length == 0 || s.Any(c => !char.IsAsciiDigit(c) && c != '.')) return false;
        var point = s.IndexOf('.');
        if (point >= 0 && (s.IndexOf('.', point + 1) >= 0 || s.Length - point - 1 > places)) return false;
        if (!decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return false;
        var factor = 1m;
        for (var i = 0; i < places; i++) factor *= 10;
        if (d * factor > long.MaxValue) return false;
        scaled = (long)(d * factor);
        return true;
    }
}
