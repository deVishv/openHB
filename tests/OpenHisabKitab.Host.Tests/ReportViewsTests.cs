namespace OpenHisabKitab.Host.Tests;

/// <summary>The Plan 6 report methods: layouts, cash flow, and paged ledger statements.</summary>
public sealed class ReportViewsTests : IDisposable
{
    static readonly DateRange Year = new FinancialYear(2026).Range;
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId cash, capital, sales, rent;

    public ReportViewsTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        capital = chart.Required(RequiredLedger.Capital);
        sales = chart.Required(RequiredLedger.Sales);
        rent = host.Masters.AddLedgerAsync(owner, company, "Rent", chart.Group(GroupKind.OtherExpenses)).GetAwaiter().GetResult();
    }

    public void Dispose() => host.Dispose();

    Task<SavedVoucher> Journal(VoucherType type, DateOnly date, LedgerId dr, LedgerId cr, long paise, string narration = "") =>
        host.Vouchers.SaveJournalAsync(owner, company,
            new JournalInput(type, date, narration, [new PostingLine(dr, new Money(paise)), new PostingLine(cr, -new Money(paise))]));

    [Fact]
    public async Task Balance_sheet_layout_fills_the_previous_year_from_the_second_year()
    {
        await Journal(VoucherType.Receipt, new(2026, 5, 1), cash, capital, 500000);
        var first = host.Reports.BalanceSheetLayout(owner, company, new(2026, 6, 30), LayoutStyle.ScheduleIII);
        Assert.False(first.HasPrevious);

        var second = host.Reports.BalanceSheetLayout(owner, company, new(2027, 5, 31), LayoutStyle.ScheduleIII);
        Assert.True(second.HasPrevious);
        var cashLine = second.Lines.Single(l => l.Kind == LayoutLineKind.CashAndEquivalents);
        Assert.Equal(new Money(500000), cashLine.Current);
        Assert.Equal(new Money(500000), cashLine.Previous);

        var pnl = host.Reports.ProfitAndLossLayout(owner, company, new FinancialYear(2027).Range, LayoutStyle.Chart);
        Assert.True(pnl.HasPrevious);
        Assert.Equal(LayoutStyle.Chart, pnl.Style);
    }

    [Fact]
    public async Task Cash_flow_reconciles_after_saves()
    {
        await Journal(VoucherType.Receipt, new(2026, 5, 1), cash, sales, 900000);
        await Journal(VoucherType.Payment, new(2026, 5, 2), rent, cash, 200000);
        var cf = host.Reports.CashFlow(owner, company, Year);
        Assert.Equal(new Money(700000), cf.NetChange);
        Assert.Equal(cf.ClosingCash - cf.OpeningCash, cf.NetChange);
        var bs = host.Reports.BalanceSheetLayout(owner, company, Year.To, LayoutStyle.ScheduleIII);
        Assert.Equal(bs.Lines.Single(l => l.Kind == LayoutLineKind.CashAndEquivalents).Current, cf.ClosingCash);
    }

    [Fact]
    public async Task Statement_pages_continue_the_running_balance()
    {
        // Saved through VoucherService, not seeded, because each line names its voucher from the database.
        for (var i = 1; i <= 203; i++) await Journal(VoucherType.Receipt, new(2026, 6, 1), cash, capital, i * 100, $"receipt {i}");

        var pages = new List<StatementPage>();
        string? after = null;
        do
        {
            var page = host.Reports.StatementPage(owner, company, cash, Year, new PageRequest(100, after));
            pages.Add(page);
            after = page.Next;
        } while (after is not null);

        Assert.Equal(new[] { 100, 100, 3 }, pages.Select(p => p.Lines.Count));
        Assert.Equal(Money.Zero, pages[0].Opening);
        for (var p = 1; p < pages.Count; p++) Assert.Equal(pages[p - 1].Lines[^1].Row.Balance, pages[p].Opening);
        var closing = host.Reports.TrialBalance(owner, company, Year).Rows.Single(r => r.Ledger == cash).Closing;
        Assert.Equal(closing, pages[^1].Lines[^1].Row.Balance);
        Assert.All(pages, p => Assert.Equal(closing, p.Closing));
        Assert.Equal(203, pages.SelectMany(p => p.Lines).Select(l => l.Row.VoucherId).Distinct().Count());
    }

    [Fact]
    public async Task Statement_line_names_its_voucher_and_counter_ledgers()
    {
        var saved = await Journal(VoucherType.Receipt, new(2026, 6, 1), cash, sales, 12345, "counter sale");
        var line = Assert.Single(host.Reports.StatementPage(owner, company, cash, Year, new PageRequest()).Lines);
        Assert.Equal(VoucherType.Receipt, line.Type);
        Assert.Equal(saved.DisplayNumber, line.Number);
        Assert.Equal("counter sale", line.Narration);
        Assert.Equal(new[] { sales }, line.Counter);
        Assert.Equal(new Money(12345), line.Row.Amount);
    }

    [Fact]
    public async Task Receipt_saved_between_pages_is_not_repeated_or_skipped()
    {
        for (var i = 1; i <= 4; i++) await Journal(VoucherType.Receipt, new(2026, 6, i), cash, capital, 100);
        var first = host.Reports.StatementPage(owner, company, cash, Year, new PageRequest(2));
        await Journal(VoucherType.Receipt, new(2026, 5, 1), cash, capital, 1000);
        var second = host.Reports.StatementPage(owner, company, cash, Year, new PageRequest(2, first.Next));

        Assert.Equal(new[] { new DateOnly(2026, 6, 3), new DateOnly(2026, 6, 4) }, second.Lines.Select(l => l.Row.Date));
        Assert.Equal(new Money(1200), second.Opening);
        Assert.Null(second.Next);
    }

    [Fact]
    public void Data_entry_is_refused_every_new_report()
    {
        var asha = host.NewUser("asha", BuiltInRole.DataEntry, company).Context;
        Action[] calls =
        [
            () => host.Reports.BalanceSheetLayout(asha, company, Year.To, LayoutStyle.ScheduleIII),
            () => host.Reports.ProfitAndLossLayout(asha, company, Year, LayoutStyle.ScheduleIII),
            () => host.Reports.CashFlow(asha, company, Year),
            () => host.Reports.StatementPage(asha, company, cash, Year, new PageRequest()),
        ];
        foreach (var call in calls) Assert.Equal(ServiceError.PermissionDenied, Assert.Throws<ServiceException>(call).Error);
    }

    [Fact]
    public void Period_across_years_is_refused_with_one_line()
    {
        var across = new DateRange(new(2026, 6, 1), new(2027, 5, 31));
        Action[] calls =
        [
            () => host.Reports.ProfitAndLossLayout(owner, company, across, LayoutStyle.ScheduleIII),
            () => host.Reports.CashFlow(owner, company, across),
            () => host.Reports.StatementPage(owner, company, cash, across, new PageRequest()),
        ];
        foreach (var call in calls) Assert.Equal("Choose a period within one financial year.", Assert.Throws<ServiceException>(call).Message);
    }
}
