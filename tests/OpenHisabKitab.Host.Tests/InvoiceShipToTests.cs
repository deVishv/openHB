namespace OpenHisabKitab.Host.Tests;

/// <summary>Ship to on issued documents, and the buyer's and company's details saved on each invoice (Plan 6b).</summary>
public sealed class InvoiceShipToTests : IDisposable
{
    static readonly DateOnly June1 = new(2026, 6, 1);
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId sales, purchases, customer, supplier;

    public InvoiceShipToTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        sales = chart.Required(RequiredLedger.Sales);
        purchases = chart.Required(RequiredLedger.Purchases);
        customer = host.Masters.SavePartyAsync(owner, company, "Ravi Stores", chart.Group(GroupKind.TradeReceivables),
            new Party(default, TestHost.G("27AAACR5055K1Z7"), new StateCode("27"), 0, "4 Station Road, Pune", 0)).GetAwaiter().GetResult();
        supplier = host.Masters.SavePartyAsync(owner, company, "Pune Steel", chart.Group(GroupKind.TradePayables),
            new Party(default, null, new StateCode("27"), 0, "", 0)).GetAwaiter().GetResult();
    }

    public void Dispose() => host.Dispose();

    static InvoiceEntryLine Line(LedgerId ledger) =>
        new(null, ledger, "Steel rod", "7214", new Quantity(1000), Money.Parse("1000.00"), Money.Zero, new GstRate(1800), new GstRate(0), false);

    InvoiceInput Sale(ShipTo? shipTo, InvoiceKind kind = InvoiceKind.Regular, string pos = "27") =>
        new(VoucherType.Sale, June1, "", customer, TradeDirection.Outward, kind, new StateCode(pos), false,
            null, null, null, null, [Line(sales)], ShipTo: shipTo);

    async Task<string> Refused(InvoiceInput input) =>
        (await Assert.ThrowsAsync<ServiceException>(() => host.Vouchers.SaveInvoiceAsync(owner, company, input))).Message;

    [Fact]
    public async Task Ship_to_is_saved_and_returned()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company,
            Sale(new ShipTo("  Ravi Godown ", "Plot 9, MIDC\nChakan ", new StateCode("27"), null)));
        Assert.Equal(new ShipTo("Ravi Godown", "Plot 9, MIDC\nChakan", new StateCode("27"), null),
            host.Vouchers.Get(owner, company, saved.Id).Invoice!.ShipTo);
        var blank = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(new ShipTo(" ", "", null, " ")));
        Assert.Null(host.Vouchers.Get(owner, company, blank.Id).Invoice!.ShipTo);
    }

    [Fact]
    public async Task Ship_to_is_refused_on_purchases()
    {
        var purchase = new InvoiceInput(VoucherType.Purchase, June1, "", supplier, TradeDirection.Inward, InvoiceKind.Regular,
            new StateCode("27"), false, "B-1", June1, null, null, [Line(purchases)],
            ShipTo: new ShipTo("Us", "Pune", new StateCode("27"), null));
        Assert.Equal("Ship to is only for documents this business issues.", await Refused(purchase));
    }

    [Fact]
    public async Task Ship_to_needs_name_and_address()
    {
        Assert.Equal("Enter the ship to name and address.", await Refused(Sale(new ShipTo("Godown", " ", new StateCode("27"), null))));
        Assert.Equal("Ship to name is too long.", await Refused(Sale(new ShipTo(new string('n', 101), "Pune", new StateCode("27"), null))));
        Assert.Equal("Ship to address is too long.", await Refused(Sale(new ShipTo("Godown", new string('a', 301), new StateCode("27"), null))));
        Assert.Equal("Ship to cannot contain control characters.", await Refused(Sale(new ShipTo("God\town", "Pune", new StateCode("27"), null))));
        Assert.Equal("Ship to cannot contain control characters.", await Refused(Sale(new ShipTo("Godown", "Pune\tEast", new StateCode("27"), null))));
    }

    [Fact]
    public async Task Country_only_for_exports_and_state_needed_otherwise()
    {
        Assert.Equal("Country of destination is only for exports.", await Refused(Sale(new ShipTo("Godown", "Pune", new StateCode("27"), "India"))));
        Assert.Equal("Choose the ship to state.", await Refused(Sale(new ShipTo("Godown", "Pune", null, null))));
        Assert.Equal("Ship to country is too long.", await Refused(
            Sale(new ShipTo("Gulf LLC", "Jebel Ali", null, new string('c', 61)), InvoiceKind.ExportUnderLut, "96")));
        Assert.Equal("Choose the ship to state.", await Refused(Sale(new ShipTo("Godown", "Pune", new StateCode("98"), null))));
        var export = await host.Vouchers.SaveInvoiceAsync(owner, company,
            Sale(new ShipTo("Gulf LLC", "Jebel Ali, Dubai", null, "United Arab Emirates"), InvoiceKind.ExportUnderLut, "96"));
        Assert.Equal("United Arab Emirates", host.Vouchers.Get(owner, company, export.Id).Invoice!.ShipTo!.Country);
    }

    [Fact]
    public async Task Invoice_keeps_party_and_company_details_as_saved()
    {
        var saved = await host.Vouchers.SaveInvoiceAsync(owner, company, Sale(null));
        var first = host.Vouchers.Get(owner, company, saved.Id).Invoice!;
        Assert.Equal(new DocumentParty("Ravi Stores", "27AAACR5055K1Z7", "4 Station Road, Pune", new StateCode("27")), first.PartyDetails);
        Assert.Equal(new DocumentParty("A Traders", "27AAPFU0939F1ZV", "1 MG Road, Pune", new StateCode("27")), first.CompanyDetails);

        var chart = host.Masters.Chart(owner, company);
        var party = host.Masters.Parties(owner, company).Single(p => p.Ledger == customer);
        await host.Masters.SavePartyAsync(owner, company, "Ravi Stores", chart.Group(GroupKind.TradeReceivables),
            party with { Gstin = TestHost.G("27AAPFU0939F1ZV"), Address = "New Market, Pune" });
        await host.Companies.UpdateAsync(owner, company, TestHost.Profile() with { Address = "2 FC Road, Pune" });

        var kept = host.Vouchers.Get(owner, company, saved.Id);
        Assert.Equal(first.PartyDetails, kept.Invoice!.PartyDetails);
        Assert.Equal(first.CompanyDetails, kept.Invoice.CompanyDetails);

        await host.Vouchers.EditInvoiceAsync(owner, company, saved.Id, kept.RowVersion, Sale(null));
        var edited = host.Vouchers.Get(owner, company, saved.Id).Invoice!;
        Assert.Equal(new DocumentParty("Ravi Stores", "27AAPFU0939F1ZV", "New Market, Pune", new StateCode("27")), edited.PartyDetails);
        Assert.Equal("2 FC Road, Pune", edited.CompanyDetails!.Address);
    }
}
