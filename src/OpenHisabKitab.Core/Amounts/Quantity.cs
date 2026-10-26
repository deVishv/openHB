namespace OpenHisabKitab.Core.Amounts;

/// <summary>A quantity in thousandths of a unit.</summary>
public readonly record struct Quantity(long Thousandths)
{
    /// <summary>Exact product of this quantity and a unit price, in paise, before rounding.</summary>
    public decimal Times(Money unitPrice) => (decimal)Thousandths * unitPrice.Paise / 1000m;
}
