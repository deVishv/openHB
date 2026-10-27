using System.Globalization;

namespace OpenHisabKitab.Core.Time;

/// <summary>Parses a reporting period typed at the prompt.</summary>
public static class PeriodInput
{
    /// <summary>
    /// One token: a month name, q1 to q4, fy, or fyNN. Two tokens: a from date and a to date in one financial year.
    /// Months and quarters fall inside the financial year containing today.
    /// </summary>
    public static bool TryParse(IReadOnlyList<string> tokens, DateOnly today, out DateRange range)
    {
        range = default;
        var fy = FinancialYear.Containing(today);

        if (tokens.Count == 2)
        {
            if (!DateInput.TryParse(tokens[0], today, out var from) || !DateInput.TryParse(tokens[1], today, out var to)) return false;
            if (from > to || FinancialYear.Containing(from) != FinancialYear.Containing(to)) return false;
            range = new DateRange(from, to);
            return true;
        }
        if (tokens.Count != 1) return false;

        var t = tokens[0].Trim().ToLowerInvariant();
        if (t == "fy") { range = fy.Range; return true; }
        if (t.Length == 4 && t.StartsWith("fy", StringComparison.Ordinal) && t[2..].All(char.IsAsciiDigit))
        {
            range = new FinancialYear(2000 + int.Parse(t[2..], CultureInfo.InvariantCulture)).Range;
            return true;
        }
        if (t.Length == 2 && t[0] == 'q' && t[1] is >= '1' and <= '4')
        {
            range = fy.Quarter(t[1] - '0');
            return true;
        }

        var month = MonthNumber(t);
        if (month == 0) return false;
        range = fy.Month(month);
        return true;
    }

    static int MonthNumber(string name)
    {
        if (name.Equals("sept", StringComparison.OrdinalIgnoreCase)) return 9;
        var format = CultureInfo.InvariantCulture.DateTimeFormat;
        for (var m = 1; m <= 12; m++)
        {
            if (name.Equals(format.GetMonthName(m), StringComparison.OrdinalIgnoreCase)
                || name.Equals(format.GetAbbreviatedMonthName(m), StringComparison.OrdinalIgnoreCase))
                return m;
        }
        return 0;
    }
}
