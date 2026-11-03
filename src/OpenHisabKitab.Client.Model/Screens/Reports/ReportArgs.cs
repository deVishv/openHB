using System.Globalization;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// Reads report arguments typed at the prompt (spec 6.2): a name, then a period or an "as at" date at the end. Drill-down
/// commands always carry two explicit dates, so a name that ends in a word like "apr" keeps it.
/// </summary>
public static class ReportArgs
{
    /// <summary>Trailing period tokens (two dates, or one period word) come off the end; the rest is the name.</summary>
    public static (string Name, DateRange? Period, string? Error) NameAndPeriod(IReadOnlyList<string> args, DateOnly today)
    {
        if (args.Count >= 2 && DateInput.TryParse(args[^2], today, out var from) && DateInput.TryParse(args[^1], today, out var to))
        {
            var name = Join(args.Take(args.Count - 2));
            if (to < from) return (name, null, Text.PeriodReversed);
            if (FinancialYear.Containing(from) != FinancialYear.Containing(to)) return (name, null, Text.PeriodAcrossYears);
            return (name, new DateRange(from, to), null);
        }
        if (args.Count >= 1 && PeriodInput.TryParse([args[^1]], today, out var range))
            return (Join(args.Take(args.Count - 1)), range, null);
        return (Join(args), null, null);
    }

    /// <summary>A trailing date comes off the end; the rest is returned as typed.</summary>
    public static (string Others, DateOnly? AsAt, string? Error) RestAndDate(IReadOnlyList<string> args, DateOnly today) =>
        args.Count >= 1 && DateInput.TryParse(args[^1], today, out var date)
            ? (Join(args.Take(args.Count - 1)), date, null)
            : (Join(args), null, null);

    /// <summary>The open year from its start to the working date, or the whole year when the working date is outside it.</summary>
    public static DateRange DefaultPeriod(ShellContext ctx)
    {
        var year = Year(ctx);
        return year.Range.Contains(ctx.WorkingDate) ? new DateRange(year.Start, ctx.WorkingDate) : year.Range;
    }

    /// <summary>The working date, or the open year's end when the working date is outside it.</summary>
    public static DateOnly DefaultAsAt(ShellContext ctx)
    {
        var year = Year(ctx);
        return year.Range.Contains(ctx.WorkingDate) ? ctx.WorkingDate : year.End;
    }

    /// <summary>A drill-down command such as "LEDG Rent 01-04-2026 31-03-2027".</summary>
    public static string Command(string code, string name, DateRange period) =>
        Join([code, name, Date(period.From), Date(period.To)]);

    /// <summary>A drill-down command with an "as at" date, such as "OUTS payable Ravi Stores 31-07-2026".</summary>
    public static string Command(string code, string name, DateOnly asAt) => Join([code, name, Date(asAt)]);

    static string Date(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    static FinancialYear Year(ShellContext ctx) => ctx.Year ?? FinancialYear.Containing(ctx.WorkingDate);

    static string Join(IEnumerable<string> parts) => string.Join(' ', parts.Where(p => p.Length > 0));
}
