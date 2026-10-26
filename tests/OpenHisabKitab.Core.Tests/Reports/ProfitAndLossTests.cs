namespace OpenHisabKitab.Core.Tests.Reports;

public class ProfitAndLossTests
{
    readonly BooksFixture f = new();

    static Money M(long paise) => new(paise);

    [Fact]
    public void Fy_2025()
    {
        var p = f.Books.ProfitAndLoss(new FinancialYear(2025).Range);
        Assert.Equal(new[] { new ReportRow(f.Sales, M(1000000)) }, p.Income);
        Assert.Equal(new[] { new ReportRow(f.Rent, M(200000)) }, p.Expenses);
        Assert.Equal(M(1000000), p.TotalIncome);
        Assert.Equal(M(200000), p.TotalExpenses);
        Assert.Equal(M(800000), p.NetProfit);
    }

    [Fact]
    public void Fy_2026_starts_from_zero()
    {
        var p = f.Books.ProfitAndLoss(new FinancialYear(2026).Range);
        Assert.Equal(new[] { new ReportRow(f.Sales, M(300000)) }, p.Income);
        Assert.Empty(p.Expenses);
        Assert.Equal(M(300000), p.NetProfit);
    }

    [Fact]
    public void Period_crossing_31_march_throws() =>
        Assert.Throws<ArgumentException>(() => f.Books.ProfitAndLoss(new DateRange(new(2026, 3, 1), new(2026, 4, 30))));
}
