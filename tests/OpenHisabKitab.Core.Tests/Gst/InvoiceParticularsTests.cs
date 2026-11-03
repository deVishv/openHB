using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Core.Tests.Gst;

public sealed class InvoiceParticularsTests
{
    static readonly GstData Gst = GstData.LoadEmbedded();
    static readonly DocumentParty Company = new("A Traders", "24AAAPA1234A1Z5", "1 Ring Road, Surat", new StateCode("24"));
    static readonly DocumentParty Walkin = new("Cash buyer", null, "", new StateCode("24"));
    const string NeedsAddress = "Add the buyer's address in PRTY, or a Ship to address, before printing.";

    static ParticularsInput Sale(Money taxable, DocumentParty? buyer = null, GstRegistration reg = GstRegistration.Regular,
        VoucherType type = VoucherType.Sale, InvoiceKind kind = InvoiceKind.Regular, bool taxFree = false, ShipTo? shipTo = null,
        string? original = null, DateOnly? originalDate = null, DocumentParty? company = null, IReadOnlyList<string>? lines = null) =>
        new(type, reg, kind, taxFree, new DateOnly(2026, 5, 1), company ?? Company, buyer ?? Walkin, shipTo, taxable,
            original, originalDate, lines ?? ["Steel rod"]);

    [Fact]
    public void Unregistered_buyer_at_fifty_thousand_without_address_is_refused()
    {
        Assert.Equal(NeedsAddress, InvoiceParticulars.FirstMissing(Sale(new Money(5000000)), Gst));
        Assert.Null(InvoiceParticulars.FirstMissing(Sale(new Money(4999999)), Gst));
        var ship = new ShipTo("Cash buyer", "2 Market, Surat", new StateCode("24"), null);
        Assert.Null(InvoiceParticulars.FirstMissing(Sale(new Money(5000000), shipTo: ship), Gst));
    }

    [Fact]
    public void Note_to_unregistered_buyer_needs_address_at_any_value() =>
        Assert.Equal(NeedsAddress, InvoiceParticulars.FirstMissing(
            Sale(new Money(100), type: VoucherType.CreditNote, original: "INV/1", originalDate: new DateOnly(2026, 4, 2)), Gst));

    [Fact]
    public void Registered_buyer_without_address_is_refused() =>
        Assert.Equal(NeedsAddress, InvoiceParticulars.FirstMissing(
            Sale(new Money(100), buyer: new DocumentParty("B Stores", "27AAAPB1234B1Z5", " ", new StateCode("27"))), Gst));

    [Fact]
    public void Unregistered_company_sale_at_fifty_thousand_prints_without_buyer_address() =>
        Assert.Null(InvoiceParticulars.FirstMissing(Sale(new Money(7500000), reg: GstRegistration.Unregistered,
            company: Company with { Gstin = null, Address = "" }, lines: [""]), Gst));

    [Fact]
    public void Bill_of_supply_follows_rule_49_particulars()
    {
        Assert.Null(InvoiceParticulars.FirstMissing(Sale(new Money(7500000), reg: GstRegistration.Composition, taxFree: true), Gst));
        Assert.Null(InvoiceParticulars.FirstMissing(Sale(new Money(7500000), taxFree: true), Gst));
        var registered = new DocumentParty("B Stores", "24AAAPB1234B1Z5", "", new StateCode("24"));
        Assert.Equal(NeedsAddress, InvoiceParticulars.FirstMissing(
            Sale(new Money(7500000), buyer: registered, reg: GstRegistration.Composition, taxFree: true), Gst));
    }

    [Fact]
    public void Note_without_original_invoice_is_refused()
    {
        var buyer = new DocumentParty("B Stores", "24AAAPB1234B1Z5", "Main Road", new StateCode("24"));
        Assert.Equal("Add the original invoice number and date before printing.",
            InvoiceParticulars.FirstMissing(Sale(new Money(100), buyer: buyer, type: VoucherType.DebitNote, original: "INV/1"), Gst));
        Assert.Null(InvoiceParticulars.FirstMissing(
            Sale(new Money(100), buyer: buyer, type: VoucherType.DebitNote, original: "INV/1", originalDate: new DateOnly(2026, 4, 1)), Gst));
    }

    [Fact]
    public void Export_without_country_is_refused()
    {
        var buyer = new DocumentParty("Gulf LLC", null, "Dubai", new StateCode("96"));
        Assert.Equal("Add the country of destination under Ship to before printing.",
            InvoiceParticulars.FirstMissing(Sale(new Money(100), buyer: buyer, kind: InvoiceKind.ExportUnderLut, taxFree: true), Gst));
        var ship = new ShipTo("Gulf LLC", "Jebel Ali", null, "United Arab Emirates");
        Assert.Null(InvoiceParticulars.FirstMissing(
            Sale(new Money(100), buyer: buyer, kind: InvoiceKind.ExportUnderLut, taxFree: true, shipTo: ship), Gst));
    }

    [Fact]
    public void Line_without_description_is_refused() =>
        Assert.Equal("Line 2: add a description before printing.",
            InvoiceParticulars.FirstMissing(Sale(new Money(100), lines: ["Steel rod", "  "]), Gst));

    [Fact]
    public void Blank_company_address_is_refused_first() =>
        Assert.Equal("Add the company address in CMPY before printing.",
            InvoiceParticulars.FirstMissing(Sale(new Money(5000000), company: Company with { Address = "" }, lines: [""]), Gst));

    [Fact]
    public void Threshold_is_data() =>
        Assert.Equal(new Money(5000000), Gst.UnregisteredDetailsThreshold(new DateOnly(2026, 5, 1)));
}
