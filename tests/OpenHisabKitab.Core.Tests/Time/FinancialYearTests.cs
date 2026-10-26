namespace OpenHisabKitab.Core.Tests.Time;

public class FinancialYearTests
{
    [Fact]
    public void Containing()
    {
        Assert.Equal(2026, FinancialYear.Containing(new DateOnly(2027, 3, 31)).StartYear);
        Assert.Equal(2027, FinancialYear.Containing(new DateOnly(2027, 4, 1)).StartYear);
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("2026-27", new FinancialYear(2026).Label);
        Assert.Equal("26-27", new FinancialYear(2026).ShortLabel);
        Assert.Equal("2099-00", new FinancialYear(2099).Label);
    }

    [Fact]
    public void Start_end_and_range()
    {
        var fy = new FinancialYear(2026);
        Assert.Equal(new DateOnly(2026, 4, 1), fy.Start);
        Assert.Equal(new DateOnly(2027, 3, 31), fy.End);
        Assert.Equal(new DateRange(fy.Start, fy.End), fy.Range);
    }

    [Fact]
    public void Quarter_and_month()
    {
        Assert.Equal(new DateRange(new(2026, 4, 1), new(2026, 6, 30)), new FinancialYear(2026).Quarter(1));
        Assert.Equal(new DateRange(new(2027, 1, 1), new(2027, 3, 31)), new FinancialYear(2026).Quarter(4));
        Assert.Equal(new DateRange(new(2027, 2, 1), new(2027, 2, 28)), new FinancialYear(2026).Month(2));
        Assert.Equal(new DateRange(new(2028, 2, 1), new(2028, 2, 29)), new FinancialYear(2027).Month(2));
    }

    [Fact]
    public void DateRange_rejects_reversed_dates() =>
        Assert.Throws<ArgumentException>(() => new DateRange(new(2026, 5, 1), new(2026, 4, 1)));

    [Fact]
    public void DateRange_contains_is_inclusive()
    {
        var r = new DateRange(new(2026, 4, 1), new(2026, 4, 30));
        Assert.True(r.Contains(new(2026, 4, 1)));
        Assert.True(r.Contains(new(2026, 4, 30)));
        Assert.False(r.Contains(new(2026, 5, 1)));
    }
}
