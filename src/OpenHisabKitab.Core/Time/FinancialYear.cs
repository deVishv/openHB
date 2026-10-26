using System.Globalization;

namespace OpenHisabKitab.Core.Time;

/// <summary>An Indian financial year, 1 April to 31 March, identified by the year it starts in.</summary>
public readonly record struct FinancialYear(int StartYear)
{
    public DateOnly Start => new(StartYear, 4, 1);
    public DateOnly End => new(StartYear + 1, 3, 31);
    public DateRange Range => new(Start, End);

    public string Label => string.Create(CultureInfo.InvariantCulture, $"{StartYear}-{(StartYear + 1) % 100:D2}");
    public string ShortLabel => string.Create(CultureInfo.InvariantCulture, $"{StartYear % 100:D2}-{(StartYear + 1) % 100:D2}");

    public static FinancialYear Containing(DateOnly d) => new(d.Month >= 4 ? d.Year : d.Year - 1);

    /// <summary>Quarter 1 is April to June; quarter 4 is January to March.</summary>
    public DateRange Quarter(int q)
    {
        if (q is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(q));
        var startMonth = q == 4 ? 1 : 1 + q * 3;
        return new DateRange(Month(startMonth).From, Month(startMonth + 2).To);
    }

    /// <summary>The calendar month that falls inside this financial year.</summary>
    public DateRange Month(int calendarMonth)
    {
        if (calendarMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(calendarMonth));
        var year = calendarMonth >= 4 ? StartYear : StartYear + 1;
        return new DateRange(new DateOnly(year, calendarMonth, 1),
            new DateOnly(year, calendarMonth, DateTime.DaysInMonth(year, calendarMonth)));
    }
}
