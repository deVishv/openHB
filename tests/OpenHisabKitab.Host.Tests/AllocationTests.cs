namespace OpenHisabKitab.Host.Tests;

public sealed class AllocationTests : IDisposable
{
    static readonly DateOnly June1 = new(2026, 6, 1);
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly ChartOfAccounts chart;
    readonly LedgerId cash, sales, purchases, customer, otherCustomer, supplier;

    public AllocationTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
        purchases = chart.Required(RequiredLedger.Purchases);
        customer = Party("Ravi Stores", GroupKind.TradeReceivables);
        otherCustomer = Party("Blr Mart", GroupKind.TradeReceivables);
        supplier = Party("Pune Steel", GroupKind.TradePayables);
    }

    public void Dispose() => host.Dispose();

    LedgerId Party(string name, GroupKind group) =>
        host.Masters.SavePartyAsync(owner, company, name, host.Masters.Chart(owner, company).Group(group),
            new Party(default, null, new StateCode("27"), 30, "", 0)).GetAwaiter().GetResult();

    static InvoiceEntryLine Line(LedgerId ledger, string price, int rateBp = 0) =>
        new(null, ledger, "Steel rod", "7214", new Quantity(1000), Money.Parse(price), Money.Zero, new GstRate(rateBp), new GstRate(0), false);

    static InvoiceInput Invoice(VoucherType type, LedgerId party, TradeDirection direction, string price, LedgerId ledger, DateOnly? date = null) =>
        new(type, date ?? June1, "", party, direction, InvoiceKind.Regular, new StateCode("27"), false, null, null, null, null, [Line(ledger, price)]);

    async Task<SavedVoucher> SaleOf(string price, LedgerId? party = null, DateOnly? date = null) =>
        await host.Vouchers.SaveInvoiceAsync(owner, company, Invoice(VoucherType.Sale, party ?? customer, TradeDirection.Outward, price, sales, date));

    static BillRef Bill(SavedVoucher v) => new(BillSource.Voucher, v.Id);

    JournalInput Receipt(string amount, LedgerId? party = null, params AllocationRow[] allocations) =>
        new(VoucherType.Receipt, new DateOnly(2026, 6, 10), "", [new PostingLine(cash, Money.Parse(amount)), new PostingLine(party ?? customer, -Money.Parse(amount))],
            allocations);

    AllocationRow A(BillRef bill, string amount, LedgerId? party = null) => new(party ?? customer, bill, Money.Parse(amount));

    Money Open(BillRef bill, LedgerId? party = null) =>
        host.Vouchers.OpenBills(owner, company, party ?? customer, null).Single(b => b.Ref == bill).Open;

    async Task<string> Refused(Func<Task> act, ServiceError error = ServiceError.Invalid)
    {
        var e = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, e.Error);
        return e.Message;
    }

    [Fact]
    public async Task Receipt_allocated_to_a_sale_reduces_its_open_amount()
    {
        var sale = await SaleOf("10000.00");
        var r = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("4000.00", null, A(Bill(sale), "4000.00")));
        Assert.Equal(Money.Parse("6000.00"), Open(Bill(sale)));
        Assert.Equal([A(Bill(sale), "4000.00")], host.Vouchers.Get(owner, company, r.Id).Allocations);
    }

    [Fact]
    public async Task Open_bills_list_due_dates_from_credit_days()
    {
        var sale = await SaleOf("100.00");
        var bill = host.Vouchers.OpenBills(owner, company, customer, null).Single();
        Assert.Equal((Bill(sale), "INV/26-27/00001", new DateOnly(2026, 7, 1)), (bill.Ref, bill.Number, bill.DueDate!.Value));
    }

    [Fact]
    public async Task Unallocated_amount_stays_on_account()
    {
        var sale = await SaleOf("1000.00");
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("1500.00", null, A(Bill(sale), "1000.00")));
        Assert.Equal(Money.Zero, Open(Bill(sale)));
    }

    [Fact]
    public async Task Only_settling_types_carry_allocations()
    {
        var sale = await SaleOf("1000.00");
        var journal = new JournalInput(VoucherType.Journal, June1, "", [new PostingLine(sales, Money.Parse("100.00")), new PostingLine(customer, Money.Parse("-100.00"))],
            [A(Bill(sale), "100.00")]);
        Assert.Equal("Only receipts, payments, and credit and debit notes settle bills.",
            await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, journal)));
        var withSale = Invoice(VoucherType.Sale, customer, TradeDirection.Outward, "50.00", sales) with { Allocations = [A(Bill(sale), "50.00")] };
        Assert.Equal("Only receipts, payments, and credit and debit notes settle bills.",
            await Refused(() => host.Vouchers.SaveInvoiceAsync(owner, company, withSale)));
    }

    [Fact]
    public async Task Allocation_to_a_ledger_that_is_not_a_party_is_refused()
    {
        var sale = await SaleOf("1000.00");
        Assert.Equal("Unknown party.", await Refused(() => host.Vouchers.SaveJournalAsync(owner, company,
            Receipt("100.00", null, new AllocationRow(sales, Bill(sale), Money.Parse("100.00"))))));
    }

    [Fact]
    public async Task Allocation_for_a_party_on_the_bill_side_is_refused()
    {
        var sale = await SaleOf("1000.00");
        var refund = new JournalInput(VoucherType.Payment, June1, "", [new PostingLine(customer, Money.Parse("100.00")), new PostingLine(cash, Money.Parse("-100.00"))],
            [A(Bill(sale), "100.00")]);
        Assert.Equal("This voucher does not settle bills of Ravi Stores.", await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, refund)));
    }

    [Fact]
    public async Task Allocation_errors_use_the_host_words()
    {
        var sale = await SaleOf("1000.00");
        var other = await SaleOf("500.00", otherCustomer);
        async Task<string> Try(params AllocationRow[] a) => await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, Receipt("2000.00", null, a)));
        Assert.Equal("Enter an allocation more than zero.", await Try(A(Bill(sale), "0.00")));
        Assert.Equal("Unknown bill.", await Try(A(new BillRef(BillSource.Voucher, 9999), "1.00")));
        Assert.Equal("Bill INV/26-27/00002 is not a bill of Ravi Stores.", await Try(A(Bill(other), "1.00")));
        Assert.Equal("Bill INV/26-27/00001 is allocated twice.", await Try(A(Bill(sale), "1.00"), A(Bill(sale), "1.00")));
        Assert.Equal("Bill INV/26-27/00001 has only 1,000.00 open.", await Try(A(Bill(sale), "1000.01")));
        var small = Receipt("100.00", null, A(Bill(sale), "200.00"));
        Assert.Equal("Bill INV/26-27/00001 has only 1,000.00 open.", await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, small with
        {
            Allocations = [A(Bill(sale), "1000.01")],
        })));
        Assert.Equal("Allocations are more than the 100.00 this voucher settles for Ravi Stores.",
            await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, small)));
    }

    [Fact]
    public async Task Credit_note_settles_the_original_invoice()
    {
        var sale = await SaleOf("1000.00");
        var note = Invoice(VoucherType.CreditNote, customer, TradeDirection.Outward, "300.00", sales) with
        {
            OriginalNumber = "INV/26-27/00001", OriginalDate = June1, Allocations = [A(Bill(sale), "300.00")],
        };
        await host.Vouchers.SaveInvoiceAsync(owner, company, note);
        Assert.Equal(Money.Parse("700.00"), Open(Bill(sale)));
    }

    [Fact]
    public async Task Debit_note_received_from_a_supplier_is_a_bill_and_cannot_carry_allocations()
    {
        var purchase = await host.Vouchers.SaveInvoiceAsync(owner, company, Invoice(VoucherType.Purchase, supplier, TradeDirection.Inward, "1000.00", purchases));
        var note = Invoice(VoucherType.DebitNote, supplier, TradeDirection.Inward, "200.00", purchases);
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, note);
        Assert.Contains(host.Vouchers.OpenBills(owner, company, supplier, null), b => b.Ref == Bill(saved) && b.Open == Money.Parse("200.00"));
        Assert.Equal("This voucher does not settle bills of Pune Steel.", await Refused(() => host.Vouchers.SaveInvoiceAsync(owner, company,
            note with { Allocations = [A(Bill(purchase), "200.00", supplier)] })));
    }

    [Fact]
    public async Task Editing_a_receipt_replaces_its_allocations()
    {
        var first = await SaleOf("1000.00");
        var second = await SaleOf("1000.00");
        var r = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("1000.00", null, A(Bill(first), "1000.00")));
        // Its own earlier allocation does not count against the bill it settles again.
        await host.Vouchers.EditJournalAsync(owner, company, r.Id, r.RowVersion, Receipt("1000.00", null, A(Bill(first), "600.00"), A(Bill(second), "400.00")));
        Assert.Equal(Money.Parse("400.00"), Open(Bill(first)));
        Assert.Equal(Money.Parse("600.00"), Open(Bill(second)));
    }

    [Fact]
    public async Task Parallel_receipts_cannot_over_settle_a_bill()
    {
        var sale = await SaleOf("10000.00");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            try
            {
                await host.Vouchers.SaveJournalAsync(owner, company, Receipt("10000.00", null, A(Bill(sale), "10000.00")));
                return null;
            }
            catch (ServiceException e) { return e.Message; }
        })));
        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r == "Bill INV/26-27/00001 has only 0.00 open.");
        Assert.Equal(Money.Zero, Open(Bill(sale)));
    }

    [Fact]
    public async Task Editing_a_settled_sale_below_its_settled_amount_is_refused()
    {
        var sale = await SaleOf("10000.00");
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("6000.00", null, A(Bill(sale), "6000.00")));
        Assert.Equal("This bill has 6,000.00 settled. Change those vouchers first.", await Refused(() =>
            host.Vouchers.EditInvoiceAsync(owner, company, sale.Id, sale.RowVersion, Invoice(VoucherType.Sale, customer, TradeDirection.Outward, "5000.00", sales))));
        await host.Vouchers.EditInvoiceAsync(owner, company, sale.Id, sale.RowVersion, Invoice(VoucherType.Sale, customer, TradeDirection.Outward, "6000.00", sales));
    }

    [Fact]
    public async Task Changing_the_party_of_a_settled_sale_is_refused()
    {
        var sale = await SaleOf("10000.00");
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("100.00", null, A(Bill(sale), "100.00")));
        Assert.Equal("This bill has 100.00 settled. Change those vouchers first.", await Refused(() =>
            host.Vouchers.EditInvoiceAsync(owner, company, sale.Id, sale.RowVersion, Invoice(VoucherType.Sale, otherCustomer, TradeDirection.Outward, "10000.00", sales))));
    }

    [Fact]
    public async Task Cancelling_a_settled_sale_is_refused()
    {
        var sale = await SaleOf("10000.00");
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("100.00", null, A(Bill(sale), "100.00")));
        Assert.Equal("This bill has 100.00 settled. Change those vouchers first.",
            await Refused(() => host.Vouchers.CancelAsync(owner, company, sale.Id, sale.RowVersion, "")));
    }

    [Fact]
    public async Task Cancelling_a_receipt_frees_its_bills()
    {
        var sale = await SaleOf("10000.00");
        var r = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("100.00", null, A(Bill(sale), "100.00")));
        await host.Vouchers.CancelAsync(owner, company, r.Id, r.RowVersion, "");
        Assert.Equal(Money.Parse("10000.00"), Open(Bill(sale)));
        await host.Vouchers.CancelAsync(owner, company, sale.Id, sale.RowVersion, "");
    }

    [Fact]
    public async Task Receipt_settles_an_opening_bill()
    {
        var v = host.Masters.OpeningBills(owner, company, customer).Version;
        await host.Masters.SetOpeningBillsAsync(owner, company, customer, v,
            [new OpeningBillRow(0, default, "OB-1", new DateOnly(2026, 3, 1), null, Money.Parse("500.00"))]);
        var ob = new BillRef(BillSource.Opening, host.Masters.OpeningBills(owner, company, customer).Bills.Single().Id);
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("200.00", null, A(ob, "200.00")));
        Assert.Equal(Money.Parse("300.00"), Open(ob));
    }

    [Fact]
    public async Task Retried_save_with_the_same_request_id_returns_the_first_voucher()
    {
        long Audit()
        {
            using var c = host.Hub.Open(company);
            return AuditLog.Verify(c).Count;
        }
        var input = Receipt("100.00") with { RequestId = "draft-1" };
        var first = await host.Vouchers.SaveJournalAsync(owner, company, input);
        var auditAfterFirst = Audit();
        var again = await host.Vouchers.SaveJournalAsync(owner, company, input with { Lines = Receipt("999.00").Lines });
        Assert.False(first.AlreadySaved);
        Assert.Equal(first with { AlreadySaved = true }, again);
        Assert.Single(host.Vouchers.List(owner, company, new FinancialYear(2026).Range, VoucherType.Receipt));
        Assert.Equal(auditAfterFirst, Audit());
        var next = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("1.00"));
        Assert.Equal("RCT/26-27/00002", next.DisplayNumber);
    }

    [Fact]
    public async Task Request_id_of_another_user_or_type_is_refused()
    {
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt("100.00") with { RequestId = "draft-1" });
        var payment = new JournalInput(VoucherType.Payment, June1, "", [new PostingLine(supplier, Money.Parse("1.00")), new PostingLine(cash, Money.Parse("-1.00"))],
            RequestId: "draft-1");
        Assert.Equal("This request id was already used for another voucher.", await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, payment)));
        var accountant = host.NewUser("acc", BuiltInRole.Accountant, company).Context;
        Assert.Equal("This request id was already used for another voucher.",
            await Refused(() => host.Vouchers.SaveJournalAsync(accountant, company, Receipt("100.00") with { RequestId = "draft-1" })));
    }

    [Fact]
    public async Task Request_id_longer_than_64_is_refused() =>
        Assert.Equal("Request id is too long.",
            await Refused(() => host.Vouchers.SaveJournalAsync(owner, company, Receipt("1.00") with { RequestId = new string('x', 65) })));

    InvoiceInput Export(InvoiceKind kind, string? port, string? sbn, DateOnly? sbd) =>
        Invoice(VoucherType.Sale, customer, TradeDirection.Outward, "1000.00", sales) with
        {
            Kind = kind, PlaceOfSupply = new StateCode("96"), Export = new ExportDetails(port, sbn, sbd),
        };

    [Fact]
    public async Task Export_details_are_stored_with_the_port_in_upper_case()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Export(InvoiceKind.ExportUnderLut, "innsa1", "1234567", June1));
        Assert.Equal(new ExportDetails("INNSA1", "1234567", June1), host.Vouchers.Get(owner, company, saved.Id).Invoice!.Export);
    }

    [Theory]
    [InlineData("Regular", "INNSA1", null, false, "Port code and shipping bill are only for exports.")]
    [InlineData("ExportWithTax", "INNS1", null, false, "Port code must be 6 letters or digits.")]
    [InlineData("ExportWithTax", "INNS-1", null, false, "Port code must be 6 letters or digits.")]
    [InlineData("ExportWithTax", null, "12", true, "Shipping bill number must be 3 to 7 digits.")]
    [InlineData("ExportWithTax", null, "12345678", true, "Shipping bill number must be 3 to 7 digits.")]
    [InlineData("ExportWithTax", null, "1234", false, "Enter both the shipping bill number and date.")]
    [InlineData("ExportWithTax", null, null, true, "Enter both the shipping bill number and date.")]
    public async Task Bad_export_details_are_refused(string kind, string? port, string? sbn, bool withDate, string message) =>
        Assert.Equal(message, await Refused(() => host.Vouchers.SaveInvoiceAsync(owner, company,
            Export(Enum.Parse<InvoiceKind>(kind), port, sbn, withDate ? June1 : null))));
    [Fact]
    public async Task Editing_without_allocations_keeps_the_stored_ones()
    {
        var sale = await SaleOf("1000.00");
        var r = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("400.00", null, A(Bill(sale), "400.00")));
        var noAllocations = Receipt("400.00") with { Narration = "fixed", Allocations = null };
        await host.Vouchers.EditJournalAsync(owner, company, r.Id, r.RowVersion, noAllocations);
        Assert.Equal([A(Bill(sale), "400.00")], host.Vouchers.Get(owner, company, r.Id).Allocations);
        Assert.Equal(Money.Parse("600.00"), Open(Bill(sale)));
    }

    [Fact]
    public async Task Editing_with_an_empty_allocation_list_frees_the_bills()
    {
        var sale = await SaleOf("1000.00");
        var r = await host.Vouchers.SaveJournalAsync(owner, company, Receipt("400.00", null, A(Bill(sale), "400.00")));
        await host.Vouchers.EditJournalAsync(owner, company, r.Id, r.RowVersion, Receipt("400.00") with { Allocations = [] });
        Assert.Equal(Money.Parse("1000.00"), Open(Bill(sale)));
    }
}
