using OpenHisabKitab.Core.Tests.Fixtures;

namespace OpenHisabKitab.Core.Tests.Reports;

/// <summary>Every expected figure here was calculated by hand, not by the software.</summary>
public class GoldenCompanyTests
{
    readonly Books books;
    readonly IReadOnlyDictionary<string, LedgerId> l;

    public GoldenCompanyTests() => (books, _, l) = GoldenCompany.Build();

    static Money Dr(long rupees) => Money.FromRupees(rupees);
    static Money Cr(long rupees) => -Money.FromRupees(rupees);

    static Money Closing(TrialBalance tb, LedgerId ledger) => tb.Rows.Single(r => r.Ledger == ledger).Closing;

    [Fact]
    public void Trial_balance_fy_2025()
    {
        var tb = books.TrialBalance(new FinancialYear(2025).Range);
        Assert.Equal(Dr(52720), Closing(tb, l["Cash"]));
        Assert.Equal(Cr(50000), Closing(tb, l["Capital"]));
        Assert.Equal(Cr(10000), Closing(tb, l["Sales"]));
        Assert.Equal(Cr(900), Closing(tb, l["OutputCgst"]));
        Assert.Equal(Cr(900), Closing(tb, l["OutputSgst"]));
        Assert.Equal(Dr(6000), Closing(tb, l["Purchases"]));
        Assert.Equal(Dr(540), Closing(tb, l["InputCgst"]));
        Assert.Equal(Dr(540), Closing(tb, l["InputSgst"]));
        Assert.Equal(Dr(2000), Closing(tb, l["Rent"]));
        Assert.Equal(Money.Zero, Closing(tb, l["Gupta"]));
        Assert.Equal(Money.Zero, Closing(tb, l["Mehta"]));
        Assert.Equal(11, tb.Rows.Count);
        Assert.Equal(Dr(61800), tb.TotalDebit);
        Assert.Equal(Dr(61800), tb.TotalCredit);
    }

    [Fact]
    public void Profit_and_loss_fy_2025()
    {
        var p = books.ProfitAndLoss(new FinancialYear(2025).Range);
        Assert.Equal(Dr(10000), p.TotalIncome);
        Assert.Equal(Dr(8000), p.TotalExpenses);
        Assert.Equal(Dr(2000), p.NetProfit);
    }

    [Fact]
    public void Balance_sheet_31_march_2026()
    {
        var b = books.BalanceSheet(new(2026, 3, 31));
        Assert.Equal(new[] { new ReportRow(l["Cash"], Dr(52720)), new ReportRow(l["InputCgst"], Dr(540)), new ReportRow(l["InputSgst"], Dr(540)) },
            b.Assets);
        Assert.Equal(new[] { new ReportRow(l["Capital"], Dr(50000)), new ReportRow(l["OutputCgst"], Dr(900)), new ReportRow(l["OutputSgst"], Dr(900)) },
            b.Liabilities);
        Assert.Equal(Dr(2000), b.CurrentProfit);
        Assert.Equal(Dr(53800), b.TotalAssets);
        Assert.Equal(Dr(53800), b.TotalLiabilities);
    }

    [Fact]
    public void Trial_balance_fy_2026()
    {
        var tb = books.TrialBalance(new FinancialYear(2026).Range);
        Assert.Equal(Cr(2000), tb.Rows.Single(r => r.Ledger == l["ProfitAndLoss"]).Opening);
        Assert.Equal(Dr(53720), Closing(tb, l["Cash"]));
        Assert.Equal(Cr(1000), Closing(tb, l["Interest"]));
        Assert.DoesNotContain(tb.Rows, r => r.Ledger == l["Purchases"] || r.Ledger == l["Sales"] || r.Ledger == l["Rent"]);
        Assert.Equal(Dr(54800), tb.TotalDebit);
        Assert.Equal(Dr(54800), tb.TotalCredit);
    }

    [Fact]
    public void Balance_sheet_31_march_2027()
    {
        var b = books.BalanceSheet(new(2027, 3, 31));
        Assert.Equal(Dr(1000), b.CurrentProfit);
        Assert.Equal(Dr(54800), b.TotalAssets);
        Assert.Equal(Dr(54800), b.TotalLiabilities);
    }

    [Fact]
    public void Cash_statement_fy_2025()
    {
        var s = books.Statement(l["Cash"], new FinancialYear(2025).Range);
        Assert.Equal(Dr(50000), s.Opening);
        Assert.Equal(new[] { Dr(61800), Dr(59800), Dr(52720) }, s.Rows.Select(r => r.Balance));
        Assert.Equal(Dr(52720), s.Closing);
    }
}
