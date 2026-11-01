namespace OpenHisabKitab.Host.Tests;

public sealed class FindTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly ChartOfAccounts chart;
    readonly LedgerId cash, sales, customer;

    public FindTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
        customer = host.Masters.SavePartyAsync(owner, company, "Ravi Stores", chart.Group(GroupKind.TradeReceivables),
            new Party(default, null, new StateCode("27"), 0, "", 0)).GetAwaiter().GetResult();
    }

    public void Dispose() => host.Dispose();

    static readonly DateRange Year = new FinancialYear(2026).Range;

    Task<SavedVoucher> Receipt(string amount, string narration = "", int day = 10, LedgerId? from = null, CallContext? by = null) =>
        host.Vouchers.SaveJournalAsync(by ?? owner, company, new JournalInput(VoucherType.Receipt, new DateOnly(2026, 6, day), narration,
            [new PostingLine(cash, Money.Parse(amount)), new PostingLine(from ?? sales, -Money.Parse(amount))]));

    Task<SavedVoucher> Sale(string price) =>
        host.Vouchers.SaveInvoiceAsync(owner, company, new InvoiceInput(VoucherType.Sale, new DateOnly(2026, 6, 1), "", customer,
            TradeDirection.Outward, InvoiceKind.Regular, new StateCode("27"), false, null, null, null, null,
            [new InvoiceEntryLine(null, sales, "Rod", "7214", new Quantity(1000), Money.Parse(price), Money.Zero, new GstRate(1800), new GstRate(0), false)]));

    Page<VoucherRow> Find(string text, PageRequest? page = null, CallContext? by = null) =>
        host.Vouchers.Find(by ?? owner, company, new VoucherSearch(text, null), page ?? new PageRequest());

    async Task<string> Refused(Func<Task> act)
    {
        var e = await Assert.ThrowsAsync<ServiceException>(act);
        return e.Message;
    }

    string Refused(Action act) => Assert.Throws<ServiceException>(act).Message;

    [Fact]
    public async Task Pages_cover_every_voucher_once()
    {
        for (var i = 0; i < 1203; i++) await Receipt("1.00", day: 1 + i % 28);
        var seen = new List<long>();
        string? after = null;
        var pages = 0;
        do
        {
            var page = host.Vouchers.List(owner, company, Year, null, new PageRequest(500, after));
            seen.AddRange(page.Rows.Select(v => v.Id));
            after = page.Next;
            pages++;
        } while (after is not null);
        Assert.Equal(3, pages);
        Assert.Equal(1203, seen.Distinct().Count());
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task List_pages_in_date_order()
    {
        var late = await Receipt("1.00", day: 20);
        var early = await Receipt("1.00", day: 5);
        var page = host.Vouchers.List(owner, company, Year, null, new PageRequest(1));
        Assert.Equal(early.Id, page.Rows.Single().Id);
        Assert.Equal(late.Id, host.Vouchers.List(owner, company, Year, null, new PageRequest(1, page.Next)).Rows.Single().Id);
    }

    [Fact]
    public void Limit_over_1000_is_refused() =>
        Assert.Equal("Ask for at most 1000 rows.", Refused(() => host.Vouchers.List(owner, company, Year, null, new PageRequest(1001))));

    [Fact]
    public void A_bad_cursor_is_refused()
    {
        Assert.Equal("That page is no longer valid. Start again.",
            Refused(() => host.Vouchers.List(owner, company, Year, null, new PageRequest(10, "nonsense"))));
        Assert.Equal("That page is no longer valid. Start again.", Refused(() => host.Masters.Parties(owner, company, new PageRequest(10, "x"))));
    }

    [Fact]
    public async Task Find_matches_the_number()
    {
        await Receipt("1.00");
        var second = await Receipt("1.00");
        Assert.Equal(second.Id, Find("rct/26-27/00002").Rows.Single().Id);
    }

    [Fact]
    public async Task Find_matches_the_narration_ignoring_case()
    {
        var r = await Receipt("1.00", "Advance for MARCH");
        await Receipt("1.00", "other");
        Assert.Equal(r.Id, Find("march").Rows.Single().Id);
    }

    [Fact]
    public async Task Find_matches_a_party_by_invoice_or_posting()
    {
        var sale = await Sale("1000.00");
        var receipt = await Receipt("500.00", from: customer);
        await Receipt("1.00");
        Assert.Equal([receipt.Id, sale.Id], Find("ravi").Rows.Select(v => v.Id));
    }

    [Fact]
    public async Task Find_matches_the_amount_with_or_without_commas()
    {
        var sale = await Sale("10000.00");
        await Receipt("1.00");
        Assert.Equal(sale.Id, Find("11,800.00").Rows.Single().Id);
        Assert.Equal(sale.Id, Find("11800").Rows.Single().Id);
    }

    [Fact]
    public async Task Find_lists_newest_first_and_includes_cancelled()
    {
        var a = await Receipt("1.00", "rent", day: 5);
        var b = await Receipt("1.00", "rent", day: 20);
        await host.Vouchers.CancelAsync(owner, company, a.Id, a.RowVersion, "");
        var page = Find("rent", new PageRequest(1));
        Assert.Equal(b.Id, page.Rows.Single().Id);
        var next = Find("rent", new PageRequest(1, page.Next));
        Assert.True(next.Rows.Single().Cancelled);
        Assert.Null(next.Next);
    }

    [Fact]
    public void Find_with_no_text_is_refused() => Assert.Equal("Enter text to find.", Refused(() => Find("  ")));

    [Fact]
    public async Task Find_shows_a_data_entry_user_only_their_own_vouchers()
    {
        var clerk = host.NewUser("clerk", BuiltInRole.DataEntry, company).Context;
        await Receipt("1.00", "rent");
        var mine = await Receipt("1.00", "rent", by: clerk);
        Assert.Equal(mine.Id, Find("rent", by: clerk).Rows.Single().Id);
    }

    [Fact]
    public async Task GetByNumber_finds_a_voucher_and_refuses_an_unknown_number()
    {
        var r = await Receipt("1.00");
        Assert.Equal(r.Id, host.Vouchers.GetByNumber(owner, company, VoucherType.Receipt, "RCT/26-27/00001").Id);
        var e = Assert.Throws<ServiceException>(() => host.Vouchers.GetByNumber(owner, company, VoucherType.Sale, "RCT/26-27/00001"));
        Assert.Equal((ServiceError.NotFound, "No voucher Sale RCT/26-27/00001."), (e.Error, e.Message));
    }

    [Fact]
    public async Task Parties_and_items_page_by_id()
    {
        var group = chart.Group(GroupKind.TradeReceivables);
        for (var i = 0; i < 4; i++)
            await host.Masters.SavePartyAsync(owner, company, $"P{i}", group, new Party(default, null, new StateCode("27"), 0, "", 0));
        var first = host.Masters.Parties(owner, company, new PageRequest(3));
        var rest = host.Masters.Parties(owner, company, new PageRequest(3, first.Next));
        Assert.Equal(5, first.Rows.Concat(rest.Rows).Select(p => p.Ledger).Distinct().Count());
        Assert.Null(rest.Next);
        for (var i = 0; i < 3; i++)
            await host.Masters.SaveItemAsync(owner, company, new Item(0, $"Item {i}", "7214", false, "kg", new GstRate(1800), new GstRate(0), Money.Zero, 0));
        var items = host.Masters.Items(owner, company, new PageRequest(2));
        Assert.Equal(2, items.Rows.Count);
        Assert.Single(host.Masters.Items(owner, company, new PageRequest(2, items.Next)).Rows);
    }
}
