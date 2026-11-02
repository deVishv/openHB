using OpenHisabKitab.Core.Tests.Fixtures;

namespace OpenHisabKitab.Core.Tests.Reports;

/// <summary>Schedule III and chart layouts. Every golden figure was worked by hand from the fixture.</summary>
public class ReportLayoutTests
{
    readonly Books books;
    readonly ChartOfAccounts chart;
    readonly IReadOnlyDictionary<string, LedgerId> l;

    public ReportLayoutTests() => (books, chart, l) = GoldenCompany.Build();

    static Money R(decimal rupees) => new((long)(rupees * 100));

    LayoutLine Group(StatementLayout s, GroupKind kind) =>
        s.Lines.Single(x => x.Kind == LayoutLineKind.Group && x.Group == chart.Group(kind));

    static LayoutLine HeadingTotal(StatementLayout s, string name) =>
        s.Lines.Single(x => x.Kind == LayoutLineKind.HeadingTotal && x.Label == name);

    static LayoutLine SideTotal(StatementLayout s, LayoutSide side) =>
        s.Lines.Single(x => x.Kind == LayoutLineKind.SideTotal && x.Side == side);

    static int IndexOf(StatementLayout s, Func<LayoutLine, bool> match)
    {
        for (var i = 0; i < s.Lines.Count; i++) if (match(s.Lines[i])) return i;
        return -1;
    }

    static void Amounts(LayoutLine line, decimal current, decimal previous)
    {
        Assert.Equal(R(current), line.Current);
        Assert.Equal(R(previous), line.Previous);
    }

    StatementLayout BalanceSheet2027(LayoutStyle style) =>
        ReportLayout.BalanceSheet(chart, books.BalanceSheet(new(2027, 3, 31)), books.BalanceSheet(new(2026, 3, 31)), style);

    [Fact]
    public void Schedule_iii_balance_sheet_31_march_2027_with_previous_year()
    {
        var s = BalanceSheet2027(LayoutStyle.ScheduleIII);
        Assert.True(s.HasPrevious);

        Amounts(Group(s, GroupKind.Capital), 50000, 50000);
        Amounts(Group(s, GroupKind.ReservesAndSurplus), 1600, 2000);
        Amounts(HeadingTotal(s, "Equity"), 51600, 52000);
        Assert.DoesNotContain(s.Lines, x => x.Label == "Non Current Liabilities");
        Amounts(Group(s, GroupKind.TradePayables), 2000, 0);
        Amounts(Group(s, GroupKind.DutiesAndTaxes), 2255, 1800);
        Amounts(HeadingTotal(s, "Current Liabilities"), 4255, 1800);
        Amounts(SideTotal(s, LayoutSide.EquityAndLiabilities), 55855, 53800);

        Amounts(Group(s, GroupKind.TradeReceivables), 695, 0);
        Amounts(s.Lines.Single(x => x.Kind == LayoutLineKind.CashAndEquivalents), 53720, 52720);
        Amounts(Group(s, GroupKind.GstInputCredit), 1440, 1080);
        Amounts(HeadingTotal(s, "Current Assets"), 55855, 53800);
        Amounts(SideTotal(s, LayoutSide.Assets), 55855, 53800);

        // Equity and liabilities come first, as Schedule III lists them.
        Assert.Equal(LayoutSide.EquityAndLiabilities, s.Lines[0].Side);
        Assert.Equal(LayoutLineKind.Heading, s.Lines[0].Kind);
        Assert.Equal("Equity", s.Lines[0].Label);
        Assert.All(s.Lines.Where(x => x.Kind == LayoutLineKind.Group), x => Assert.Equal(1, x.Depth));
    }

    [Fact]
    public void First_year_has_no_previous_column()
    {
        var s = ReportLayout.BalanceSheet(chart, books.BalanceSheet(new(2026, 3, 31)), null, LayoutStyle.ScheduleIII);
        Assert.False(s.HasPrevious);
        Assert.All(s.Lines, x => Assert.Null(x.Previous));
        Assert.Equal(R(53800), SideTotal(s, LayoutSide.Assets).Current);
    }

    [Fact]
    public void Schedule_iii_profit_and_loss_fy_2026_with_previous_year()
    {
        var s = ReportLayout.ProfitAndLoss(chart, books.ProfitAndLoss(new FinancialYear(2026).Range),
            books.ProfitAndLoss(new FinancialYear(2025).Range), LayoutStyle.ScheduleIII);
        Amounts(Group(s, GroupKind.RevenueFromOperations), 600.04m, 10000);
        Amounts(Group(s, GroupKind.OtherIncome), 1000, 0);
        Amounts(SideTotal(s, LayoutSide.Income), 1600.04m, 10000);
        Amounts(Group(s, GroupKind.Purchases), 2000, 6000);
        Amounts(Group(s, GroupKind.OtherExpenses), 0.04m, 2000);
        Amounts(SideTotal(s, LayoutSide.Expenses), 2000.04m, 8000);
        Amounts(s.Lines.Single(x => x.Kind == LayoutLineKind.NetProfit), -400, 2000);
        Assert.Equal(LayoutLineKind.NetProfit, s.Lines[^1].Kind);
    }

    [Fact]
    public void Chart_style_lists_ledgers_under_their_groups()
    {
        var s = BalanceSheet2027(LayoutStyle.Chart);
        var heading = IndexOf(s, x => x.Kind == LayoutLineKind.Heading && x.Label == "Current Assets");
        Assert.Equal(0, s.Lines[heading].Depth);
        var receivables = IndexOf(s, x => x.Kind == LayoutLineKind.Group && x.Group == chart.Group(GroupKind.TradeReceivables));
        Assert.Equal(1, s.Lines[receivables].Depth);
        var gupta = s.Lines[receivables + 1];
        Assert.Equal(LayoutLineKind.Ledger, gupta.Kind);
        Assert.Equal(l["Gupta"], gupta.Ledger);
        Assert.Equal("Gupta Enterprises", gupta.Label);
        Assert.Equal(2, gupta.Depth);

        var profit = s.Lines.Single(x => x.Kind == LayoutLineKind.ProfitForPeriod);
        Assert.Equal(R(-400), profit.Current);
        Assert.Equal(R(2000), profit.Previous);
        var reserves = IndexOf(s, x => x.Kind == LayoutLineKind.Group && x.Group == chart.Group(GroupKind.ReservesAndSurplus));
        Assert.True(IndexOf(s, x => x.Kind == LayoutLineKind.ProfitForPeriod) > reserves);
        Assert.Equal(2, profit.Depth);

        Assert.Equal(R(55855), SideTotal(s, LayoutSide.EquityAndLiabilities).Current);
        Assert.Equal(R(55855), SideTotal(s, LayoutSide.Assets).Current);
        Assert.DoesNotContain(s.Lines, x => x.Kind == LayoutLineKind.CashAndEquivalents);
    }

    [Fact]
    public void Opening_difference_is_its_own_line()
    {
        var (b, c, _) = RandomBooks.Generate(4);
        Assert.NotEqual(Money.Zero, b.OpeningDifference);
        var s = ReportLayout.BalanceSheet(c, b.BalanceSheet(new(2026, 3, 31)), null, LayoutStyle.ScheduleIII);
        var line = s.Lines.Single(x => x.Kind == LayoutLineKind.OpeningDifference);
        Assert.Equal(b.OpeningDifference, line.Current);
        var total = IndexOf(s, x => x.Kind == LayoutLineKind.SideTotal && x.Side == LayoutSide.EquityAndLiabilities);
        Assert.Equal(total - 1, IndexOf(s, x => x.Kind == LayoutLineKind.OpeningDifference));
        Assert.Equal(SideTotal(s, LayoutSide.Assets).Current, SideTotal(s, LayoutSide.EquityAndLiabilities).Current);
    }

    [Fact]
    public void Asset_ledger_in_credit_stays_on_its_side()
    {
        var (c, bank) = chart.AddLedger("HDFC Bank", chart.Group(GroupKind.BankAccounts));
        var (c2, loan) = c.AddLedger("Term Loan", c.Group(GroupKind.LongTermBorrowings));
        var cash = c2.Required(RequiredLedger.Cash);
        var capital = c2.Required(RequiredLedger.Capital);
        var b = new Books(c2, new(2026, 4, 1),
            new Dictionary<LedgerId, Money> { [cash] = R(1000), [capital] = R(-1000) },
            [new PostingRecord(1, new(2026, 5, 1), loan, R(500)), new PostingRecord(1, new(2026, 5, 1), bank, R(-500))]);
        var s = ReportLayout.BalanceSheet(c2, b.BalanceSheet(new(2026, 5, 31)), null, LayoutStyle.ScheduleIII);
        var cashLine = s.Lines.Single(x => x.Kind == LayoutLineKind.CashAndEquivalents);
        Assert.Equal(LayoutSide.Assets, cashLine.Side);
        Assert.Equal(R(500), cashLine.Current);

        var chartStyle = ReportLayout.BalanceSheet(c2, b.BalanceSheet(new(2026, 5, 31)), null, LayoutStyle.Chart);
        var bankLine = chartStyle.Lines.Single(x => x.Ledger == bank);
        Assert.Equal(LayoutSide.Assets, bankLine.Side);
        Assert.Equal(R(-500), bankLine.Current);
    }

    [Theory]
    [MemberData(nameof(InvariantTests.Seeds), MemberType = typeof(InvariantTests))]
    public void Layout_side_totals_equal_the_report_totals(int seed)
    {
        var (b, c, _) = RandomBooks.Generate(seed);
        foreach (var style in new[] { LayoutStyle.ScheduleIII, LayoutStyle.Chart })
        {
            var current = b.BalanceSheet(new(2027, 3, 31));
            var s = ReportLayout.BalanceSheet(c, current, b.BalanceSheet(new(2026, 3, 31)), style);
            Assert.Equal(current.TotalAssets, SideTotal(s, LayoutSide.Assets).Current);
            Assert.Equal(current.TotalLiabilities, SideTotal(s, LayoutSide.EquityAndLiabilities).Current);
            var pnl = b.ProfitAndLoss(new FinancialYear(2026).Range);
            var p = ReportLayout.ProfitAndLoss(c, pnl, null, style);
            Assert.Equal(pnl.NetProfit, p.Lines.Single(x => x.Kind == LayoutLineKind.NetProfit).Current);
            Assert.Equal(pnl.TotalIncome, SideTotal(p, LayoutSide.Income).Current);
        }
    }
}
