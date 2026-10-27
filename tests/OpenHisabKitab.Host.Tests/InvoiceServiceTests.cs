namespace OpenHisabKitab.Host.Tests;

public sealed class InvoiceServiceTests : IDisposable
{
    static readonly DateOnly June1 = new(2026, 6, 1);
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly ChartOfAccounts chart;
    readonly LedgerId sales, purchases, localCustomer, karnatakaCustomer, localSupplier;

    public InvoiceServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        chart = host.Masters.Chart(owner, company);
        sales = chart.Required(RequiredLedger.Sales);
        purchases = chart.Required(RequiredLedger.Purchases);
        localCustomer = Party(company, "Ravi Stores", GroupKind.TradeReceivables, "27");
        karnatakaCustomer = Party(company, "Blr Mart", GroupKind.TradeReceivables, "29");
        localSupplier = Party(company, "Pune Steel", GroupKind.TradePayables, "27");
    }

    public void Dispose() => host.Dispose();

    LedgerId Party(int id, string name, GroupKind group, string state) =>
        host.Masters.SaveParty(owner, id, name, host.Masters.Chart(owner, id).Group(group),
            new Party(default, null, new StateCode(state), 0, "", 0));

    static InvoiceEntryLine Line(LedgerId ledger, string price, int rateBp = 1800) =>
        new(null, ledger, "Steel rod", "7214", new Quantity(1000), Money.Parse(price), Money.Zero, new GstRate(rateBp), new GstRate(0), false);

    static InvoiceInput Sale(LedgerId party, string pos, params InvoiceEntryLine[] lines) =>
        new(VoucherType.Sale, June1, "", party, TradeDirection.Outward, InvoiceKind.Regular, new StateCode(pos), false,
            null, null, null, null, lines);

    static async Task<ServiceException> Fails(ServiceError error, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    Money Posted(long voucher, LedgerId ledger) =>
        host.Vouchers.Get(owner, company, voucher).Postings.Where(p => p.Ledger == ledger).Select(p => p.Amount).SingleOrDefault();

    [Fact]
    public async Task Intra_state_sale()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1000.00")));
        Assert.Equal("INV/26-27/00001", saved.DisplayNumber);
        var line = host.Vouchers.Get(owner, company, saved.Id).Invoice!.Lines.Single();
        Assert.Equal(Money.Parse("1000.00"), line.Taxable);
        Assert.Equal(Money.Parse("90.00"), line.Cgst);
        Assert.Equal(Money.Parse("90.00"), line.Sgst);
        Assert.Equal(Money.Parse("1180.00"), Posted(saved.Id, localCustomer));
        Assert.Equal(Money.Parse("-1000.00"), Posted(saved.Id, sales));
        Assert.Equal(Money.Parse("-90.00"), Posted(saved.Id, chart.Required(RequiredLedger.OutputCgst)));
    }

    [Fact]
    public async Task Inter_state_sale_takes_igst()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(karnatakaCustomer, "29", Line(sales, "1000.00")));
        Assert.Equal(Money.Parse("180.00"), host.Vouchers.Get(owner, company, saved.Id).Invoice!.Lines.Single().Igst);
        Assert.Equal(Money.Parse("-180.00"), Posted(saved.Id, chart.Required(RequiredLedger.OutputIgst)));
    }

    [Fact]
    public async Task Sez_without_tax_has_no_tax_but_keeps_the_rate()
    {
        var input = Sale(localCustomer, "27", Line(sales, "1000.00")) with { Kind = InvoiceKind.SezWithoutTax };
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, input);
        var line = host.Vouchers.Get(owner, company, saved.Id).Invoice!.Lines.Single();
        Assert.Equal(Money.Parse("1000.00"), line.Taxable);
        Assert.Equal(Money.Zero, line.Cgst + line.Sgst + line.Igst + line.Utgst + line.Cess);
        Assert.Equal(1800, line.Rate.BasisPoints);
    }

    [Fact]
    public async Task Intra_state_round_off()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "100.40")));
        var v = host.Vouchers.Get(owner, company, saved.Id);
        Assert.Equal(Money.Parse("9.04"), v.Invoice!.Lines.Single().Cgst);
        Assert.Equal(Money.Parse("-0.48"), v.Invoice.RoundOff);
        Assert.Equal(Money.Parse("118.00"), Posted(saved.Id, localCustomer));
        Assert.Equal(Money.Parse("0.48"), Posted(saved.Id, chart.Required(RequiredLedger.RoundOff)));
    }

    [Fact]
    public async Task Inter_state_round_off()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(karnatakaCustomer, "29", Line(sales, "100.40")));
        var v = host.Vouchers.Get(owner, company, saved.Id);
        Assert.Equal(Money.Parse("18.07"), v.Invoice!.Lines.Single().Igst);
        Assert.Equal(Money.Parse("-0.47"), v.Invoice.RoundOff);
    }

    [Fact]
    public async Task Round_off_setting_off_keeps_paise()
    {
        host.Companies.SetSetting(owner, company, "invoice.roundOff", "false");
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "100.40")));
        Assert.Equal(Money.Parse("118.48"), Posted(saved.Id, localCustomer));
    }

    [Fact]
    public async Task Stored_amounts_are_the_host_calculation()
    {
        var input = Sale(localCustomer, "27", Line(sales, "333.33"), Line(sales, "0.07", 500));
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, input);
        var expected = InvoiceCalculator.Calculate(
            [new(new Quantity(1000), Money.Parse("333.33"), Money.Zero, new GstRate(1800), new GstRate(0)),
             new(new Quantity(1000), Money.Parse("0.07"), Money.Zero, new GstRate(500), new GstRate(0))],
            TaxSplit.CgstSgst, roundOff: true, reverseCharge: false);
        var stored = host.Vouchers.Get(owner, company, saved.Id).Invoice!;
        Assert.Equal(expected.Lines.Select(l => (l.Taxable, l.Cgst, l.Sgst)), stored.Lines.Select(l => (l.Taxable, l.Cgst, l.Sgst)));
        Assert.Equal(expected.RoundOff, stored.RoundOff);
    }

    [Fact]
    public async Task Reverse_charge_on_a_sale_is_refused()
    {
        var ex = await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1000.00")) with { ReverseCharge = true }));
        Assert.Equal("Reverse charge applies only to purchases.", ex.Message);
    }

    [Fact]
    public async Task Reverse_charge_purchase_credits_rcm_payable()
    {
        var input = new InvoiceInput(VoucherType.Purchase, June1, "", localSupplier, TradeDirection.Inward, InvoiceKind.Regular,
            new StateCode("27"), true, "PS/101", June1, null, null, [Line(purchases, "1000.00")]);
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, input);
        Assert.Equal("PUR/26-27/00001", saved.DisplayNumber);
        Assert.Equal(Money.Parse("-90.00"), Posted(saved.Id, chart.Required(RequiredLedger.RcmPayableCgst)));
        Assert.Equal(Money.Parse("-90.00"), Posted(saved.Id, chart.Required(RequiredLedger.RcmPayableSgst)));
        Assert.Equal(Money.Parse("90.00"), Posted(saved.Id, chart.Required(RequiredLedger.InputCgst)));
        Assert.Equal(Money.Parse("-1000.00"), Posted(saved.Id, localSupplier));
        Assert.Equal("PS/101", host.Vouchers.Get(owner, company, saved.Id).Invoice!.PartyDocNumber);
    }

    [Fact]
    public async Task Purchase_from_another_state_takes_igst_from_the_supplier_state()
    {
        var supplier = Party(company, "Blr Steel", GroupKind.TradePayables, "29");
        var input = new InvoiceInput(VoucherType.Purchase, June1, "", supplier, TradeDirection.Inward, InvoiceKind.Regular,
            new StateCode("27"), false, null, null, null, null, [Line(purchases, "1000.00")]);
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, input);
        Assert.Equal(Money.Parse("180.00"), Posted(saved.Id, chart.Required(RequiredLedger.InputIgst)));
    }

    [Fact]
    public async Task Composition_company_sells_without_tax_and_cannot_claim_input_tax()
    {
        var comp = host.NewCompany(TestHost.Profile("C Traders") with { Registration = GstRegistration.Composition, Gstin = null });
        var compChart = host.Masters.Chart(owner, comp);
        var customer = Party(comp, "Ravi Stores", GroupKind.TradeReceivables, "27");
        var supplier = Party(comp, "Pune Steel", GroupKind.TradePayables, "27");
        var sale = await host.Vouchers.SaveInvoiceAsync(owner, comp, Sale(customer, "27", Line(compChart.Required(RequiredLedger.Sales), "1000.00")));
        Assert.Equal(Money.Zero, host.Vouchers.Get(owner, comp, sale.Id).Invoice!.Lines.Single().Cgst);

        var purchase = new InvoiceInput(VoucherType.Purchase, June1, "", supplier, TradeDirection.Inward, InvoiceKind.Regular,
            new StateCode("27"), false, null, null, null, null, [Line(compChart.Required(RequiredLedger.Purchases), "1000.00")]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, comp, purchase));
        Assert.Equal("This company cannot take input tax credit. Enter the purchase with rate 0 and the full amount.", ex.Message);
        await host.Vouchers.SaveInvoiceAsync(owner, comp, purchase with { Lines = [Line(compChart.Required(RequiredLedger.Purchases), "1180.00", 0)] });
    }

    [Fact]
    public async Task Sale_line_must_use_an_income_ledger()
    {
        var ex = await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(chart.Required(RequiredLedger.Cash), "10.00"))));
        Assert.Equal("Line 1: choose a sales or purchase ledger.", ex.Message);
        await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "10.00"), Line(chart.Required(RequiredLedger.InputCgst), "1.00"))));
    }

    [Fact]
    public async Task Purchase_line_cannot_use_a_tax_or_party_ledger()
    {
        var input = new InvoiceInput(VoucherType.Purchase, June1, "", localSupplier, TradeDirection.Inward, InvoiceKind.Regular,
            new StateCode("27"), false, null, null, null, null, [Line(chart.Required(RequiredLedger.InputCgst), "10.00")]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, company, input));
        Assert.Equal("Line 1: choose a sales or purchase ledger.", ex.Message);
        await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, company, input with { Lines = [Line(localCustomer, "10.00")] }));
    }

    [Fact]
    public async Task Outward_credit_note_reduces_sales()
    {
        var input = Sale(localCustomer, "27", Line(sales, "1000.00")) with
        {
            Type = VoucherType.CreditNote, OriginalNumber = "INV/26-27/00001", OriginalDate = June1,
        };
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, input);
        Assert.Equal("CN/26-27/00001", saved.DisplayNumber);
        Assert.Equal(Money.Parse("1000.00"), Posted(saved.Id, sales));
        Assert.Equal(Money.Parse("-1180.00"), Posted(saved.Id, localCustomer));
    }

    [Fact]
    public async Task Sale_must_be_outward()
    {
        var ex = await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1.00")) with { Direction = TradeDirection.Inward }));
        Assert.Equal("Sale must be outward and purchase inward.", ex.Message);
    }

    [Fact]
    public async Task Unknown_party_and_bad_rate_and_bad_lines()
    {
        Assert.Equal("Unknown party.", (await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(sales, "27", Line(sales, "1.00"))))).Message);
        Assert.Equal("GST rate 7% is not valid on 01-06-2026.", (await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1.00", 700))))).Message);
        Assert.Equal("Enter at least one line.", (await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27")))).Message);
        Assert.Equal("Line 2: price cannot be negative.", (await Fails(ServiceError.Invalid, () =>
            host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1.00"), Line(sales, "-1.00"))))).Message);
    }

    [Fact]
    public async Task Journal_entry_cannot_save_an_invoice_type_and_edit_recalculates()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", Line(sales, "1000.00")));
        var edited = await host.Vouchers.EditInvoiceAsync(owner, company, saved.Id, saved.RowVersion,
            Sale(karnatakaCustomer, "29", Line(sales, "2000.00")));
        Assert.Equal(saved.DisplayNumber, edited.DisplayNumber);
        Assert.Equal(Money.Parse("2360.00"), Posted(saved.Id, karnatakaCustomer));
        Assert.Equal(Money.Zero, Posted(saved.Id, localCustomer));
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, company,
            Sale(localCustomer, "27", Line(sales, "1.00")) with { Type = VoucherType.Receipt }));
        Assert.Equal("Use the journal entry for Receipt.", ex.Message);
    }

    [Theory]
    [InlineData(InvoiceKind.ExportUnderLut)]
    [InlineData(InvoiceKind.SezWithoutTax)]
    [InlineData(InvoiceKind.SezWithTax)]
    [InlineData(InvoiceKind.ExportWithTax)]
    [InlineData(InvoiceKind.DeemedExport)]
    public async Task Received_documents_must_be_regular(InvoiceKind kind)
    {
        var input = new InvoiceInput(VoucherType.Purchase, June1, "", localSupplier, TradeDirection.Inward, kind,
            new StateCode("27"), false, null, null, null, null, [Line(purchases, "1000.00")]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, company, input));
        Assert.Equal("Choose Regular for a document received from a supplier.", ex.Message);
    }

    [Fact]
    public async Task Unknown_item_is_named()
    {
        var line = Line(sales, "10.00") with { ItemId = 999 };
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveInvoiceAsync(owner, company, Sale(localCustomer, "27", line)));
        Assert.Equal("Line 1: unknown item.", ex.Message);
    }
}
