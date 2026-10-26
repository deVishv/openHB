namespace OpenHisabKitab.Core.Tests.Reports;

public class BalanceSheetTests
{
    readonly BooksFixture f = new();

    static Money M(long paise) => new(paise);

    [Fact]
    public void As_at_31_march_2026()
    {
        var b = f.Books.BalanceSheet(new(2026, 3, 31));
        Assert.Equal(new[] { new ReportRow(f.Cash, M(5800000)) }, b.Assets);
        Assert.Equal(new[] { new ReportRow(f.Capital, M(5000000)) }, b.Liabilities);
        Assert.Equal(M(800000), b.CurrentProfit);
        Assert.Equal(M(5800000), b.TotalAssets);
        Assert.Equal(M(5800000), b.TotalLiabilities);
    }

    [Fact]
    public void As_at_31_march_2027_shows_carried_profit()
    {
        var b = f.Books.BalanceSheet(new(2027, 3, 31));
        Assert.Equal(new[] { new ReportRow(f.Capital, M(5000000)), new ReportRow(f.ProfitAndLoss, M(800000)) }, b.Liabilities);
        Assert.Equal(M(300000), b.CurrentProfit);
        Assert.Equal(M(6100000), b.TotalAssets);
        Assert.Equal(M(6100000), b.TotalLiabilities);
    }

    [Fact]
    public void Opening_difference_keeps_sides_equal()
    {
        var g = new BooksFixture(capitalOpeningPaise: -4999000);
        var b = g.Books.BalanceSheet(new(2026, 3, 31));
        Assert.Equal(M(1000), b.OpeningDifference);
        Assert.Equal(b.TotalAssets, b.TotalLiabilities);
    }

    [Theory] // invariant 4
    [InlineData(2025)]
    [InlineData(2026)]
    public void Net_profit_matches_current_profit(int startYear)
    {
        var fy = new FinancialYear(startYear);
        Assert.Equal(f.Books.ProfitAndLoss(fy.Range).NetProfit, f.Books.BalanceSheet(fy.End).CurrentProfit);
    }

    [Fact]
    public void Mid_year_date_uses_profit_to_that_date()
    {
        var b = f.Books.BalanceSheet(new(2025, 4, 15));
        Assert.Equal(M(1000000), b.CurrentProfit);
        Assert.Equal(b.TotalAssets, b.TotalLiabilities);
    }
}
