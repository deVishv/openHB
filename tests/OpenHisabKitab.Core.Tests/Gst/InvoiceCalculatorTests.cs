namespace OpenHisabKitab.Core.Tests.Gst;

public class InvoiceCalculatorTests
{
    static InvoiceLineInput L(long qtyThousandths, long pricePaise, int bp, long discount = 0, int cess = 0, bool incl = false) =>
        new(new Quantity(qtyThousandths), new Money(pricePaise), new Money(discount), new GstRate(bp), new GstRate(cess), incl);

    [Fact]
    public void Intra_state_18_percent_with_round_off()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(3000, 33333, 1800) }, TaxSplit.CgstSgst, roundOff: true, reverseCharge: false);
        Assert.Equal(new Money(99999), r.Taxable);
        Assert.Equal(new Money(9000), r.Cgst);
        Assert.Equal(new Money(9000), r.Sgst);
        Assert.Equal(new Money(1), r.RoundOff);
        Assert.Equal(new Money(118000), r.GrandTotal);
    }

    [Fact]
    public void Each_half_rounds_separately()
    {
        var intra = InvoiceCalculator.Calculate(new[] { L(1000, 30, 1800) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(new Money(3), intra.Cgst);
        Assert.Equal(new Money(3), intra.Sgst);
        var inter = InvoiceCalculator.Calculate(new[] { L(1000, 30, 1800) }, TaxSplit.Igst, false, false);
        Assert.Equal(new Money(5), inter.Igst);
        Assert.Equal(Money.Zero, inter.Cgst);
    }

    [Fact]
    public void Quarter_percent_half_rate() =>
        Assert.Equal(new Money(125),
            InvoiceCalculator.Calculate(new[] { L(1000, 100000, 25) }, TaxSplit.CgstSgst, false, false).Cgst);

    [Fact]
    public void Utgst_split()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 10005, 500) }, TaxSplit.CgstUtgst, false, false);
        Assert.Equal(new Money(250), r.Cgst);
        Assert.Equal(new Money(250), r.Utgst);
        Assert.Equal(Money.Zero, r.Sgst);
    }

    [Fact]
    public void Discount_rounds_once() =>
        Assert.Equal(new Money(48998),
            InvoiceCalculator.Calculate(new[] { L(2500, 19999, 0, discount: 1000) }, TaxSplit.Igst, false, false).Taxable);

    [Fact]
    public void Cess() =>
        Assert.Equal(new Money(1200),
            InvoiceCalculator.Calculate(new[] { L(1000, 10000, 2800, cess: 1200) }, TaxSplit.Igst, false, false).Cess);

    [Theory]
    [InlineData(123450, 50, 123500)]
    [InlineData(123449, -49, 123400)]
    public void Round_off_to_rupee(long taxable, long roundOff, long grand)
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, taxable, 0) }, TaxSplit.Igst, true, false);
        Assert.Equal(new Money(roundOff), r.RoundOff);
        Assert.Equal(new Money(grand), r.GrandTotal);
    }

    [Fact]
    public void Round_off_off_leaves_paise()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 123449, 0) }, TaxSplit.Igst, false, false);
        Assert.Equal(Money.Zero, r.RoundOff);
        Assert.Equal(new Money(123449), r.GrandTotal);
    }

    [Fact]
    public void Inclusive_price_matches_typed_total()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 10000, 1800, incl: true) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(new Money(8474), r.Taxable);
        Assert.Equal(new Money(763), r.Cgst);
        Assert.Equal(new Money(763), r.Sgst);
        Assert.Equal(new Money(10000), r.GrandTotal);
    }

    [Fact]
    public void Inclusive_price_with_exact_split()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 11800, 1800, incl: true) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(new Money(10000), r.Taxable);
        Assert.Equal(new Money(900), r.Cgst);
        Assert.Equal(new Money(11800), r.GrandTotal);
    }

    [Fact]
    public void Reverse_charge_total_excludes_tax()
    {
        var r = InvoiceCalculator.Calculate(new[] { L(1000, 100000, 1800) }, TaxSplit.Igst, false, true);
        Assert.Equal(new Money(18000), r.Igst);
        Assert.Equal(new Money(100000), r.GrandTotal);
    }

    [Fact] // invariant 7
    public void Totals_equal_sum_of_lines()
    {
        var r = InvoiceCalculator.Calculate(
            new[] { L(1000, 30, 1800), L(1000, 33333, 1800), L(7000, 1999, 500) }, TaxSplit.CgstSgst, false, false);
        Assert.Equal(Money.Sum(r.Lines.Select(l => l.Cgst)), r.Cgst);
        Assert.Equal(Money.Sum(r.Lines.Select(l => l.Sgst)), r.Sgst);
        Assert.Equal(Money.Sum(r.Lines.Select(l => l.Taxable)), r.Taxable);
        Assert.Equal(r.Taxable + r.Tax, r.GrandTotal);
        Assert.Equal(r.Lines[0].Taxable + r.Lines[0].Tax, r.Lines[0].Total);
    }

    [Theory]
    [InlineData(0, 100, 0, InvoiceError.NonPositiveQuantity)]
    [InlineData(-1000, 100, 0, InvoiceError.NonPositiveQuantity)]
    [InlineData(1000, -1, 0, InvoiceError.NegativePrice)]
    [InlineData(1000, 100, -1, InvoiceError.NegativeDiscount)]
    [InlineData(1000, 100, 101, InvoiceError.NegativeTaxable)]
    public void Rejects_bad_lines(long q, long p, long disc, InvoiceError e)
    {
        var ex = Assert.Throws<InvoiceException>(() =>
            InvoiceCalculator.Calculate(new[] { L(q, p, 1800, disc) }, TaxSplit.Igst, false, false));
        Assert.Equal(e, ex.Error);
        Assert.Equal(0, ex.LineIndex);
    }

    [Fact]
    public void Rejects_empty() =>
        Assert.Equal(InvoiceError.NoLines, Assert.Throws<InvoiceException>(() =>
            InvoiceCalculator.Calculate(Array.Empty<InvoiceLineInput>(), TaxSplit.Igst, false, false)).Error);
}
