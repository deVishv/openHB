namespace OpenHisabKitab.Host.Tests;

/// <summary>OUTS in the host: bills as at a date against the books (spec 5.1; Plan 6 Task 4 rules).</summary>
public sealed class OutstandingServiceTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId cash, sales, purchases, ravi, blr, pune;

    public OutstandingServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
        purchases = chart.Required(RequiredLedger.Purchases);
        ravi = Party("Ravi Stores", GroupKind.TradeReceivables);
        blr = Party("Blr Mart", GroupKind.TradeReceivables);
        pune = Party("Pune Steel", GroupKind.TradePayables);
    }

    public void Dispose() => host.Dispose();

    LedgerId Party(string name, GroupKind group) =>
        host.Masters.SavePartyAsync(owner, company, name, host.Masters.Chart(owner, company).Group(group),
            new Party(default, null, new StateCode("27"), 30, "", 0)).GetAwaiter().GetResult();

    Task<SavedVoucher> Invoice(VoucherType type, LedgerId party, string price, DateOnly date) =>
        host.Vouchers.SaveInvoiceAsync(owner, company, new InvoiceInput(type, date, "", party,
            type == VoucherType.Sale ? TradeDirection.Outward : TradeDirection.Inward, InvoiceKind.Regular, new StateCode("27"), false,
            null, null, null, null,
            [new InvoiceEntryLine(null, type == VoucherType.Sale ? sales : purchases, "Steel rod", "7214", new Quantity(1000),
                Money.Parse(price), Money.Zero, new GstRate(0), new GstRate(0), false)]));

    Task<SavedVoucher> Cash(VoucherType type, LedgerId party, string amount, DateOnly date, params AllocationRow[] allocations)
    {
        var a = Money.Parse(amount);
        PostingLine[] lines = type == VoucherType.Receipt
            ? [new PostingLine(cash, a), new PostingLine(party, -a)]
            : [new PostingLine(party, a), new PostingLine(cash, -a)];
        return host.Vouchers.SaveJournalAsync(owner, company, new JournalInput(type, date, "", lines, allocations));
    }

    static AllocationRow A(LedgerId party, SavedVoucher bill, string amount) => new(party, new BillRef(BillSource.Voucher, bill.Id), Money.Parse(amount));

    async Task Seed()
    {
        await host.Masters.SetOpeningAsync(owner, company, ravi, Money.Parse("2000.00"));
        await host.Masters.SetOpeningAsync(owner, company, host.Masters.Chart(owner, company).Required(RequiredLedger.Capital), Money.Parse("-2000.00"));
        await host.Masters.SetOpeningBillsAsync(owner, company, ravi, host.Masters.OpeningBills(owner, company, ravi).Version,
            [new OpeningBillRow(0, ravi, "OB-1", new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 15), Money.Parse("2000.00"))]);
        var raviSale = await Invoice(VoucherType.Sale, ravi, "10000.00", new(2026, 6, 1));
        await Cash(VoucherType.Receipt, ravi, "4000.00", new(2026, 6, 10), A(ravi, raviSale, "4000.00"));
        await Cash(VoucherType.Receipt, ravi, "1000.00", new(2026, 7, 15));
        await Invoice(VoucherType.Sale, blr, "500.00", new(2026, 6, 1));
        // A discount allowed by journal: nothing allocates it, so it stays on account.
        await host.Vouchers.SaveJournalAsync(owner, company, new JournalInput(VoucherType.Journal, new(2026, 6, 20), "discount",
            [new PostingLine(sales, Money.Parse("100.00")), new PostingLine(blr, Money.Parse("-100.00"))]));
        var purchase = await Invoice(VoucherType.Purchase, pune, "3000.00", new(2026, 6, 5));
        await Cash(VoucherType.Payment, pune, "1000.00", new(2026, 7, 1), A(pune, purchase, "1000.00"));
    }

    [Fact]
    public async Task Open_bills_plus_on_account_equal_the_ledger_closing()
    {
        await Seed();
        var books = host.Books.Get(company);
        foreach (var date in new DateOnly[] { new(2026, 6, 5), new(2026, 6, 30), new(2026, 7, 31) })
            foreach (var side in new[] { PartySide.Receivable, PartySide.Payable })
            {
                var r = host.Reports.Outstanding(owner, company, date, side);
                foreach (var p in r.Parties)
                {
                    var closing = books.Closing(p.Party, date);
                    var expected = side == PartySide.Receivable ? closing : -closing;
                    Assert.Equal(expected, Money.Sum(p.Bills.Select(b => b.Bill.Open)) + p.OnAccount);
                    Assert.Equal(expected, p.Total);
                }
            }

        // As at 5 June the receipt of 10 June has not happened, so the sale is open in full.
        var early = host.Reports.Outstanding(owner, company, new(2026, 6, 5), PartySide.Receivable);
        var raviEarly = early.Parties.Single(p => p.Party == ravi);
        Assert.Equal(Money.Parse("10000.00"), raviEarly.Bills.Single(b => b.Bill.Number == "INV/26-27/00001").Bill.Open);
        Assert.Equal(Money.Zero, raviEarly.OnAccount);

        var late = host.Reports.Outstanding(owner, company, new(2026, 7, 31), PartySide.Receivable);
        Assert.Equal(new[] { blr, ravi }, late.Parties.Select(p => p.Party));
        var raviLate = late.Parties.Single(p => p.Party == ravi);
        Assert.Equal(Money.Parse("-1000.00"), raviLate.OnAccount);
        Assert.Equal(Money.Parse("7000.00"), raviLate.Total);
        Assert.Equal(Money.Parse("-100.00"), late.Parties.Single(p => p.Party == blr).OnAccount);

        var payable = Assert.Single(host.Reports.Outstanding(owner, company, new(2026, 7, 31), PartySide.Payable).Parties);
        Assert.Equal((pune, Money.Parse("2000.00"), Money.Zero), (payable.Party, payable.Total, payable.OnAccount));
    }

    [Fact]
    public async Task Ageing_follows_the_company_setting()
    {
        await Invoice(VoucherType.Sale, blr, "500.00", new(2026, 6, 1));
        await host.Companies.SetSettingAsync(owner, company, "outstanding.ageingDays", "\"15,45\"");
        var r = host.Reports.Outstanding(owner, company, new(2026, 7, 21), PartySide.Receivable);
        Assert.Equal(new[] { 15, 45 }, r.Ageing.Days);
        var party = Assert.Single(r.Parties);
        Assert.Equal(4, party.Buckets.Count);
        var bill = Assert.Single(party.Bills);
        Assert.Equal((20, 2), (bill.DaysOverdue, bill.Bucket));
    }

    [Fact]
    public void Data_entry_is_refused_outstanding()
    {
        var asha = host.NewUser("asha", BuiltInRole.DataEntry, company).Context;
        var e = Assert.Throws<ServiceException>(() => host.Reports.Outstanding(asha, company, new(2026, 7, 31), PartySide.Receivable));
        Assert.Equal(ServiceError.PermissionDenied, e.Error);
    }
}
