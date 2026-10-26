using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Gst;

/// <summary>Calculates GST invoice lines and totals under the single rounding rule in spec 5.3.</summary>
public static class InvoiceCalculator
{
    public static InvoiceResult Calculate(IReadOnlyList<InvoiceLineInput> lines, TaxSplit split, bool roundOff, bool reverseCharge)
    {
        if (lines.Count == 0) throw new InvoiceException(InvoiceError.NoLines, null);

        var results = lines.Select((line, i) => CalculateLine(line, i, split)).ToList();
        var taxable = Money.Sum(results.Select(r => r.Taxable));
        var cgst = Money.Sum(results.Select(r => r.Cgst));
        var sgst = Money.Sum(results.Select(r => r.Sgst));
        var utgst = Money.Sum(results.Select(r => r.Utgst));
        var igst = Money.Sum(results.Select(r => r.Igst));
        var cess = Money.Sum(results.Select(r => r.Cess));

        // Under reverse charge the supplier is paid only the taxable value.
        var raw = reverseCharge ? taxable : taxable + cgst + sgst + utgst + igst + cess;
        var grand = roundOff ? new Money(checked(Rounding.HalfAwayFromZero(raw.Paise / 100m) * 100)) : raw;

        return new InvoiceResult(results, taxable, cgst, sgst, utgst, igst, cess, grand - raw, grand);
    }

    static InvoiceLineResult CalculateLine(InvoiceLineInput line, int index, TaxSplit split)
    {
        if (line.Quantity.Thousandths <= 0) throw new InvoiceException(InvoiceError.NonPositiveQuantity, index);
        if (line.UnitPrice < Money.Zero) throw new InvoiceException(InvoiceError.NegativePrice, index);
        if (line.Discount < Money.Zero) throw new InvoiceException(InvoiceError.NegativeDiscount, index);

        var gross = line.Quantity.Times(line.UnitPrice) - line.Discount.Paise;
        if (gross < 0) throw new InvoiceException(InvoiceError.NegativeTaxable, index);
        var amount = Rounding.HalfAwayFromZero(gross);

        if (!line.PriceIncludesTax) return Tax(amount, line, split);

        // Derive the taxable value from a tax inclusive amount. Prefer the candidate whose
        // total equals what the user typed, so the line total never drifts by a paisa.
        var t0 = Rounding.HalfAwayFromZero(amount * 10000m / (10000 + line.Rate.BasisPoints + line.CessRate.BasisPoints));
        foreach (var t in new[] { t0, t0 - 1, t0 + 1 })
        {
            if (t < 0) continue;
            var candidate = Tax(t, line, split);
            if (candidate.Total.Paise == amount) return candidate;
        }
        return Tax(t0, line, split);
    }

    static InvoiceLineResult Tax(long taxable, InvoiceLineInput line, TaxSplit split)
    {
        Money Part(int divisor) => new(Rounding.HalfAwayFromZero(taxable * (decimal)line.Rate.BasisPoints / divisor));

        var half = Part(20000);
        var cess = new Money(Rounding.HalfAwayFromZero(taxable * (decimal)line.CessRate.BasisPoints / 10000));
        var t = new Money(taxable);
        return split switch
        {
            TaxSplit.CgstSgst => new InvoiceLineResult(t, half, half, Money.Zero, Money.Zero, cess),
            TaxSplit.CgstUtgst => new InvoiceLineResult(t, half, Money.Zero, half, Money.Zero, cess),
            TaxSplit.Igst => new InvoiceLineResult(t, Money.Zero, Money.Zero, Money.Zero, Part(10000), cess),
            _ => throw new ArgumentOutOfRangeException(nameof(split)),
        };
    }
}
