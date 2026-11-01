namespace OpenHisabKitab.Core.Gst;

public enum TaxDecision { Taxed, TaxFree, RefuseUnregisteredReverseCharge, RefuseInputTax }

/// <summary>
/// Whether a document carries tax. The host enforces it and the client uses it for live totals, so both follow one rule.
/// </summary>
public static class TaxMode
{
    /// <summary>
    /// Zero tax: exports under LUT and SEZ supplies without payment, a composition company's bills of supply, and every
    /// document of an unregistered company. Composition and unregistered companies cannot take input credit, but a
    /// composition company still pays reverse charge at the normal rates (CGST Act section 9(3) and 9(4)). A business
    /// liable to pay reverse charge must register (CGST Act section 24), so an unregistered one with the flag is refused.
    /// </summary>
    public static TaxDecision Decide(GstRegistration registration, InvoiceKind kind, TradeDirection direction, bool reverseCharge, bool anyTaxedLine)
    {
        if (kind is InvoiceKind.ExportUnderLut or InvoiceKind.SezWithoutTax) return TaxDecision.TaxFree;
        if (registration == GstRegistration.Regular) return TaxDecision.Taxed;
        if (registration == GstRegistration.Unregistered && reverseCharge) return TaxDecision.RefuseUnregisteredReverseCharge;
        if (registration == GstRegistration.Composition && direction == TradeDirection.Inward && reverseCharge) return TaxDecision.Taxed;
        if (direction == TradeDirection.Inward && anyTaxedLine) return TaxDecision.RefuseInputTax;
        return TaxDecision.TaxFree;
    }
}
