using System.Globalization;

namespace OpenHisabKitab.Core.Time;

/// <summary>Parses a date typed at the prompt.</summary>
public static class DateInput
{
    // 9998 keeps the financial year containing any accepted date inside DateOnly's range.
    const int MinYear = 1900;
    const int MaxYear = 9998;

    /// <summary>
    /// Accepts dd-mm-yyyy, dd/mm/yyyy, dd-mm-yy, d-m (inside the financial year containing today), and "today".
    /// </summary>
    public static bool TryParse(string token, DateOnly today, out DateOnly date)
    {
        date = default;
        var t = token.Trim();
        if (t.Equals("today", StringComparison.OrdinalIgnoreCase)) { date = today; return true; }

        if (t.Contains('-') && t.Contains('/')) return false;
        var parts = t.Split('-', '/');
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit))) return false;
        if (parts[0].Length > 2 || parts[1].Length > 2) return false;

        var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return false;

        int year;
        if (parts.Length == 2)
        {
            year = month >= 4 ? FinancialYear.Containing(today).StartYear : FinancialYear.Containing(today).StartYear + 1;
        }
        else if (parts[2].Length == 2)
        {
            year = 2000 + int.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        else if (parts[2].Length == 4)
        {
            year = int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (year is < MinYear or > MaxYear) return false;
        }
        else
        {
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateOnly(year, month, day);
        return true;
    }
}
