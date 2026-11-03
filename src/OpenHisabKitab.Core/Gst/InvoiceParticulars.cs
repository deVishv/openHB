using System.Globalization;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Core.Gst;

/// <summary>What a printed GST document is checked against.</summary>
public sealed record ParticularsInput(VoucherType Type, GstRegistration Registration, InvoiceKind Kind, bool EveryLineTaxFree,
    DateOnly Date, DocumentParty Company, DocumentParty Buyer, ShipTo? ShipTo, Money Taxable, string? OriginalNumber,
    DateOnly? OriginalDate, IReadOnlyList<string> LineDescriptions);

/// <summary>
/// The particulars a printed document must carry that the books may lack. Only the governing rule applies: Rule 46 for
/// a tax invoice, Rule 49 (with Rule 46's provisos) for a bill of supply, and Rule 53 for a note. None applies to an
/// unregistered business. A document missing one is not printed (owner decision, 2026-11-03).
/// </summary>
public static class InvoiceParticulars
{
    public static string? FirstMissing(ParticularsInput p, GstData gst)
    {
        if (p.Registration == GstRegistration.Unregistered) return null;
        var isNote = p.Type is VoucherType.CreditNote or VoucherType.DebitNote;
        var isTaxInvoice = !isNote && InvoiceWording.Title(p.Type, p.Registration, p.Kind, p.EveryLineTaxFree) == "Tax invoice";

        if (Blank(p.Company.Address)) return "Add the company address in CMPY before printing.";
        for (var i = 0; i < p.LineDescriptions.Count; i++)
            if (Blank(p.LineDescriptions[i]))
                return string.Format(CultureInfo.InvariantCulture, "Line {0}: add a description before printing.", i + 1);
        if (isNote && (Blank(p.OriginalNumber) || p.OriginalDate is null))
            return "Add the original invoice number and date before printing.";
        if (p.Kind is InvoiceKind.ExportWithTax or InvoiceKind.ExportUnderLut && Blank(p.ShipTo?.Country))
            return "Add the country of destination under Ship to before printing.";

        var hasAddress = !Blank(p.Buyer.Address) || !Blank(p.ShipTo?.Address);
        var registeredBuyer = !Blank(p.Buyer.Gstin);
        var needsAddress = registeredBuyer                                                     // 46(d), 49(d), 53(e)
            || isNote                                                                          // 53(f)
            || (isTaxInvoice && p.Taxable >= gst.UnregisteredDetailsThreshold(p.Date));        // 46(e)
        return needsAddress && !hasAddress ? "Add the buyer's address in PRTY, or a Ship to address, before printing." : null;
    }

    static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
}
