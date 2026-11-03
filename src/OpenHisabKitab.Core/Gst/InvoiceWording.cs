using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Core.Gst;

/// <summary>
/// The legal wording on printed GST documents, quoted from the CGST Rules (checked 2026-11-03 on
/// taxinformation.cbic.gov.in). Labels such as "Invoice no." are screen text and live in the client's resources.
/// </summary>
public static class InvoiceWording
{
    /// <summary>Rule 5(1)(g): a composition taxable person says so at the top of the bill of supply.</summary>
    public const string CompositionLine = "Composition taxable person, not eligible to collect tax on supplies";

    /// <summary>
    /// The nature of the document (Rule 53(1A)(b) for notes). A Regular sale whose lines all carry no tax is a bill of
    /// supply (Rule 49); exports and SEZ supplies stay tax invoices even under LUT. An unregistered business is not a
    /// registered person, so its document is a plain invoice.
    /// </summary>
    public static string Title(VoucherType type, GstRegistration registration, InvoiceKind kind, bool everyLineTaxFree) => type switch
    {
        VoucherType.CreditNote => "Credit note",
        VoucherType.DebitNote => "Debit note",
        _ => registration switch
        {
            GstRegistration.Unregistered => "Invoice",
            GstRegistration.Composition => "Bill of supply",
            _ when kind == InvoiceKind.Regular && everyLineTaxFree => "Bill of supply",
            _ => "Tax invoice",
        },
    };

    /// <summary>The Rule 46 export and SEZ proviso endorsement, or null for other kinds.</summary>
    public static string? Endorsement(InvoiceKind kind) => kind switch
    {
        InvoiceKind.ExportWithTax => "SUPPLY MEANT FOR EXPORT ON PAYMENT OF INTEGRATED TAX",
        InvoiceKind.ExportUnderLut => "SUPPLY MEANT FOR EXPORT UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX",
        InvoiceKind.SezWithTax => "SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS ON PAYMENT OF INTEGRATED TAX",
        InvoiceKind.SezWithoutTax =>
            "SUPPLY TO SEZ UNIT OR SEZ DEVELOPER FOR AUTHORISED OPERATIONS UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX",
        _ => null,
    };

    /// <summary>Rule 48: goods in triplicate, services in duplicate; the setting "original" prints one copy.</summary>
    public static IReadOnlyList<string> CopyLabels(bool anyGoods, string copiesSetting) =>
        copiesSetting == "original" ? ["ORIGINAL FOR RECIPIENT"]
        : anyGoods ? ["ORIGINAL FOR RECIPIENT", "DUPLICATE FOR TRANSPORTER", "TRIPLICATE FOR SUPPLIER"]
        : ["ORIGINAL FOR RECIPIENT", "DUPLICATE FOR SUPPLIER"];

    /// <summary>Services are classified under SAC chapter 99.</summary>
    public static bool IsService(string hsnSac) => hsnSac.Trim().StartsWith("99", StringComparison.Ordinal);
}
