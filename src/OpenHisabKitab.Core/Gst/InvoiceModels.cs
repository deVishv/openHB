using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Gst;

public sealed record InvoiceLineInput(
    Quantity Quantity, Money UnitPrice, Money Discount, GstRate Rate, GstRate CessRate, bool PriceIncludesTax = false);

public sealed record InvoiceLineResult(Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess)
{
    public Money Tax => Cgst + Sgst + Utgst + Igst + Cess;
    public Money Total => Taxable + Tax;
}

public sealed record InvoiceResult(
    IReadOnlyList<InvoiceLineResult> Lines,
    Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess,
    Money RoundOff, Money GrandTotal)
{
    public Money Tax => Cgst + Sgst + Utgst + Igst + Cess;
}

public enum InvoiceError { NoLines, NonPositiveQuantity, NegativePrice, NegativeDiscount, NegativeTaxable }

public sealed class InvoiceException(InvoiceError error, int? lineIndex)
    : Exception(lineIndex is null ? $"Invoice error: {error}" : $"Invoice line {lineIndex + 1}: {error}")
{
    public InvoiceError Error { get; } = error;
    public int? LineIndex { get; } = lineIndex;
}
