using System.Globalization;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>An invoice's figures as the footer shows them. Total is what the party is charged; Problem is why it cannot be worked out yet.</summary>
public sealed record InvoiceTotalsView(Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess, Money RoundOff, Money Total, string? Problem)
{
    public static InvoiceTotalsView Empty { get; } = new(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, null);
}

/// <summary>
/// The host's invoice calculation, made with the same Core code (spec 4, Host service: a client may calculate for display):
/// TaxMode decides whether tax applies, GstRules.Split picks CGST and SGST, UTGST, or IGST, and InvoiceCalculator rounds each
/// line to the paisa. The host calculates again on save; the two must agree (Review Focus 4).
/// </summary>
public static class InvoiceTotals
{
    static readonly GstData Gst = GstData.LoadEmbedded();

    public static InvoiceTotalsView Calculate(CompanyProfileMsg company, StateCode partyState, InvoiceMsg draft, bool roundOff)
    {
        if (draft.Lines.Count == 0 || draft.PlaceOfSupply.Length == 0 || company.State.Length == 0) return InvoiceTotalsView.Empty;
        var direction = Enum.Parse<TradeDirection>(draft.Direction);
        var kind = Enum.Parse<InvoiceKind>(draft.Kind);
        var registration = Enum.Parse<GstRegistration>(company.Registration);
        var anyTaxed = draft.Lines.Any(l => l.RateBp != 0 || l.CessBp != 0);
        var decision = TaxMode.Decide(registration, kind, direction, draft.ReverseCharge, anyTaxed);
        if (decision == TaxDecision.RefuseUnregisteredReverseCharge) return InvoiceTotalsView.Empty with { Problem = Text.UnregisteredReverseCharge };
        if (decision == TaxDecision.RefuseInputTax) return InvoiceTotalsView.Empty with { Problem = Text.NoInputTaxCredit };
        var taxFree = decision == TaxDecision.TaxFree;

        TaxSplit split;
        try
        {
            // The supplier is this business for outward documents, and the party for inward ones.
            var supplier = direction == TradeDirection.Outward ? new StateCode(company.State) : partyState;
            var sezOrExport = kind is not (InvoiceKind.Regular or InvoiceKind.DeemedExport);
            split = GstRules.Split(supplier, new StateCode(draft.PlaceOfSupply), sezOrExport, Gst);
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException or FormatException)
        {
            return InvoiceTotalsView.Empty;
        }

        InvoiceResult r;
        try
        {
            r = InvoiceCalculator.Calculate(
                [.. draft.Lines.Select(l => new InvoiceLineInput(new Quantity(l.QtyThousandths), new Money(l.UnitPricePaise), new Money(l.DiscountPaise),
                    new GstRate(taxFree ? 0 : l.RateBp), new GstRate(taxFree ? 0 : l.CessBp), l.PriceIncludesTax))],
                split, roundOff, draft.ReverseCharge);
        }
        catch (InvoiceException e)
        {
            return InvoiceTotalsView.Empty with { Problem = Words(e) };
        }
        return new InvoiceTotalsView(r.Taxable, r.Cgst, r.Sgst, r.Utgst, r.Igst, r.Cess, r.RoundOff, r.GrandTotal, null);
    }

    // The host's words for the same refusals (VoucherService.InvoiceMessage).
    static string Words(InvoiceException e)
    {
        var text = e.Error switch
        {
            InvoiceError.NoLines => Text.InvoiceNoLines,
            InvoiceError.NonPositiveQuantity => Text.InvoiceQuantity,
            InvoiceError.NegativePrice => Text.InvoiceNegativePrice,
            InvoiceError.NegativeDiscount => Text.InvoiceNegativeDiscount,
            _ => Text.InvoiceDiscountTooBig,
        };
        return e.LineIndex is { } i ? string.Format(CultureInfo.InvariantCulture, Text.InvoiceLineProblem, i + 1, text) : text;
    }
}
