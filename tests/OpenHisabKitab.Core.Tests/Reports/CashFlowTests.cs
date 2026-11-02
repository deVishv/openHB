using OpenHisabKitab.Core.Tests.Fixtures;

namespace OpenHisabKitab.Core.Tests.Reports;

/// <summary>Cash flow by the indirect method. Every expected figure was worked by hand.</summary>
public class CashFlowTests
{
    static Money R(decimal rupees) => new((long)(rupees * 100));

    static (CashFlowSection, CashFlowItem, GroupId?, Money) L(CashFlowSection s, CashFlowItem i, GroupId? g, decimal rupees) => (s, i, g, R(rupees));

    static void Lines(CashFlowStatement cf, params (CashFlowSection, CashFlowItem, GroupId?, Money)[] expected) =>
        Assert.Equal(expected, cf.Lines.Select(x => (x.Section, x.Item, x.Group, x.Amount)));

    [Fact]
    public void Golden_cash_flow_fy_2025()
    {
        var (books, chart, _) = GoldenCompany.Build();
        var cf = books.CashFlow(new FinancialYear(2025).Range);
        Lines(cf,
            L(CashFlowSection.Operating, CashFlowItem.NetProfit, null, 2000),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.DutiesAndTaxes), 1800),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.GstInputCredit), -1080));
        Assert.Equal(R(2720), cf.Operating);
        Assert.Equal(Money.Zero, cf.Investing);
        Assert.Equal(Money.Zero, cf.Financing);
        Assert.Equal(R(2720), cf.NetChange);
        Assert.Equal(R(50000), cf.OpeningCash);
        Assert.Equal(R(52720), cf.ClosingCash);
    }

    [Fact]
    public void Golden_cash_flow_fy_2026()
    {
        var (books, chart, _) = GoldenCompany.Build();
        var cf = books.CashFlow(new FinancialYear(2026).Range);
        Lines(cf,
            L(CashFlowSection.Operating, CashFlowItem.NetProfit, null, -400),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.TradePayables), 2000),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.DutiesAndTaxes), 455),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.TradeReceivables), -695),
            L(CashFlowSection.Operating, CashFlowItem.GroupChange, chart.Group(GroupKind.GstInputCredit), -360));
        Assert.Equal(R(1000), cf.Operating);
        Assert.Equal(R(1000), cf.NetChange);
        Assert.Equal(R(52720), cf.OpeningCash);
        Assert.Equal(R(53720), cf.ClosingCash);
    }

    [Fact]
    public void Depreciation_overdraft_and_capital()
    {
        var chart = DefaultChart.Create();
        (chart, var machine) = chart.AddLedger("Machine", chart.Group(GroupKind.FixedAssets));
        (chart, var depreciation) = chart.AddLedger("Depreciation on Machine", chart.Group(GroupKind.Depreciation));
        (chart, var overdraft) = chart.AddLedger("SBI Overdraft", chart.Group(GroupKind.BankOverdraft));
        var cash = chart.Required(RequiredLedger.Cash);
        var capital = chart.Required(RequiredLedger.Capital);
        var sales = chart.Required(RequiredLedger.Sales);
        var postings = new List<PostingRecord>();
        void Pair(long id, LedgerId dr, LedgerId cr, decimal rupees)
        {
            postings.Add(new PostingRecord(id, new(2026, 5, (int)id), dr, R(rupees)));
            postings.Add(new PostingRecord(id, new(2026, 5, (int)id), cr, -R(rupees)));
        }
        Pair(1, machine, cash, 6000);
        Pair(2, depreciation, machine, 600);
        Pair(3, cash, overdraft, 3000);
        Pair(4, cash, capital, 2000);
        Pair(5, cash, sales, 5000);
        var books = new Books(chart, new(2026, 4, 1),
            new Dictionary<LedgerId, Money> { [cash] = R(10000), [capital] = -R(10000) }, postings);

        var cf = books.CashFlow(new FinancialYear(2026).Range);
        Lines(cf,
            L(CashFlowSection.Operating, CashFlowItem.NetProfit, null, 4400),
            L(CashFlowSection.Operating, CashFlowItem.Depreciation, null, 600),
            L(CashFlowSection.Investing, CashFlowItem.GroupChange, chart.Group(GroupKind.FixedAssets), -6000),
            L(CashFlowSection.Financing, CashFlowItem.GroupChange, chart.Group(GroupKind.Capital), 2000),
            L(CashFlowSection.Financing, CashFlowItem.GroupChange, chart.Group(GroupKind.ShortTermBorrowings), 3000));
        Assert.Equal(R(5000), cf.Operating);
        Assert.Equal(R(-6000), cf.Investing);
        Assert.Equal(R(5000), cf.Financing);
        Assert.Equal(R(4000), cf.NetChange);
        Assert.Equal(R(10000), cf.OpeningCash);
        Assert.Equal(R(14000), cf.ClosingCash);
    }

    [Fact]
    public void Period_across_years_is_refused()
    {
        var (books, _, _) = GoldenCompany.Build();
        Assert.Throws<ArgumentException>(() => books.CashFlow(new DateRange(new(2026, 3, 1), new(2026, 4, 30))));
    }

    [Fact]
    public void Closing_is_the_balance_as_at_a_date()
    {
        var (books, _, l) = GoldenCompany.Build();
        Assert.Equal(R(59800), books.Closing(l["Cash"], new(2025, 4, 25)));
        Assert.Equal(R(-2000), books.Closing(l["ProfitAndLoss"], new(2026, 4, 1)));
    }
}
