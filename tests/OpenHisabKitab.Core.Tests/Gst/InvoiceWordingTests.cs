using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Core.Tests.Gst;

public sealed class InvoiceWordingTests
{
    [Theory]
    [InlineData(VoucherType.Sale, GstRegistration.Regular, InvoiceKind.Regular, false, "Tax invoice")]
    [InlineData(VoucherType.Sale, GstRegistration.Regular, InvoiceKind.Regular, true, "Bill of supply")]
    [InlineData(VoucherType.Sale, GstRegistration.Regular, InvoiceKind.ExportUnderLut, true, "Tax invoice")]
    [InlineData(VoucherType.Sale, GstRegistration.Composition, InvoiceKind.Regular, true, "Bill of supply")]
    [InlineData(VoucherType.Sale, GstRegistration.Unregistered, InvoiceKind.Regular, true, "Invoice")]
    [InlineData(VoucherType.CreditNote, GstRegistration.Regular, InvoiceKind.Regular, false, "Credit note")]
    [InlineData(VoucherType.DebitNote, GstRegistration.Composition, InvoiceKind.Regular, true, "Debit note")]
    public void Titles_follow_type_and_registration(VoucherType type, GstRegistration reg, InvoiceKind kind, bool taxFree, string title) =>
        Assert.Equal(title, InvoiceWording.Title(type, reg, kind, taxFree));

    [Fact]
    public void Endorsements_quote_the_export_proviso()
    {
        Assert.Equal("SUPPLY MEANT FOR EXPORT ON PAYMENT OF INTEGRATED TAX", InvoiceWording.Endorsement(InvoiceKind.ExportWithTax));
        Assert.Equal("SUPPLY MEANT FOR EXPORT UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX",
            InvoiceWording.Endorsement(InvoiceKind.ExportUnderLut));
        Assert.Equal("SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS ON PAYMENT OF INTEGRATED TAX",
            InvoiceWording.Endorsement(InvoiceKind.SezWithTax));
        Assert.Equal("SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX",
            InvoiceWording.Endorsement(InvoiceKind.SezWithoutTax));
        Assert.Null(InvoiceWording.Endorsement(InvoiceKind.Regular));
        Assert.Null(InvoiceWording.Endorsement(InvoiceKind.DeemedExport));
        Assert.Equal("Composition taxable person, not eligible to collect tax on supplies", InvoiceWording.CompositionLine);
    }

    [Fact]
    public void Copy_labels_follow_rule_48_and_the_setting()
    {
        Assert.Equal(["ORIGINAL FOR RECIPIENT", "DUPLICATE FOR TRANSPORTER", "TRIPLICATE FOR SUPPLIER"], InvoiceWording.CopyLabels(true, "byRule"));
        Assert.Equal(["ORIGINAL FOR RECIPIENT", "DUPLICATE FOR SUPPLIER"], InvoiceWording.CopyLabels(false, "byRule"));
        Assert.Equal(["ORIGINAL FOR RECIPIENT"], InvoiceWording.CopyLabels(true, "original"));
    }

    [Theory]
    [InlineData("998314", true)]
    [InlineData("9954", true)]
    [InlineData(" 99", true)]
    [InlineData("8471", false)]
    [InlineData("", false)]
    public void Sac_codes_are_services(string code, bool service) => Assert.Equal(service, InvoiceWording.IsService(code));
}
