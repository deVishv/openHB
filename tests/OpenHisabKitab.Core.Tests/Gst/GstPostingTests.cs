namespace OpenHisabKitab.Core.Tests.Gst;

public class GstPostingTests
{
    readonly ChartOfAccounts chart;
    readonly LedgerId party;
    readonly GstPostingAccounts acc;

    public GstPostingTests()
    {
        var c = DefaultChart.Create();
        (chart, party) = c.AddLedger("Gupta Enterprises", c.Group(GroupKind.TradeReceivables));
        acc = GstPostingAccounts.FromChart(chart, party);
    }

    LedgerId R(RequiredLedger r) => chart.Required(r);

    static InvoiceResult Sale999() => InvoiceCalculator.Calculate(
        new[] { new InvoiceLineInput(new Quantity(3000), new Money(33333), Money.Zero, new GstRate(1800), new GstRate(0)) },
        TaxSplit.CgstSgst, true, false);

    [Fact]
    public void Outward_invoice()
    {
        var p = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, false, acc);
        Assert.Equal(new[]
        {
            new PostingLine(party, new Money(118000)),
            new PostingLine(R(RequiredLedger.Sales), new Money(-99999)),
            new PostingLine(R(RequiredLedger.OutputCgst), new Money(-9000)),
            new PostingLine(R(RequiredLedger.OutputSgst), new Money(-9000)),
            new PostingLine(R(RequiredLedger.RoundOff), new Money(-1)),
        }, p);
    }

    [Fact]
    public void Outward_debit_note_posts_like_invoice() =>
        Assert.Equal(
            GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, false, acc),
            GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.DebitNote, false, acc));

    [Fact]
    public void Outward_credit_note_is_exact_negation()
    {
        var inv = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, false, acc);
        var cn = GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.CreditNote, false, acc);
        Assert.Equal(inv.Select(l => l with { Amount = -l.Amount }), cn);
    }

    [Fact]
    public void Inward_invoice()
    {
        var r = InvoiceCalculator.Calculate(
            new[] { new InvoiceLineInput(new Quantity(1000), new Money(600000), Money.Zero, new GstRate(1800), new GstRate(0)) },
            TaxSplit.CgstSgst, true, false);
        var p = GstPosting.Post(r, new[] { R(RequiredLedger.Purchases) }, TradeDirection.Inward, GstDocumentKind.Invoice, false, acc);
        Assert.Equal(new[]
        {
            new PostingLine(R(RequiredLedger.Purchases), new Money(600000)),
            new PostingLine(R(RequiredLedger.InputCgst), new Money(54000)),
            new PostingLine(R(RequiredLedger.InputSgst), new Money(54000)),
            new PostingLine(party, new Money(-708000)),
        }, p);
    }

    [Fact]
    public void Inward_reverse_charge()
    {
        var r = InvoiceCalculator.Calculate(
            new[] { new InvoiceLineInput(new Quantity(1000), new Money(100000), Money.Zero, new GstRate(1800), new GstRate(0)) },
            TaxSplit.Igst, false, true);
        var p = GstPosting.Post(r, new[] { R(RequiredLedger.Purchases) }, TradeDirection.Inward, GstDocumentKind.Invoice, true, acc);
        Assert.Equal(new[]
        {
            new PostingLine(R(RequiredLedger.Purchases), new Money(100000)),
            new PostingLine(R(RequiredLedger.InputIgst), new Money(18000)),
            new PostingLine(R(RequiredLedger.RcmPayableIgst), new Money(-18000)),
            new PostingLine(party, new Money(-100000)),
        }, p);
    }

    [Fact]
    public void Lines_with_same_ledger_are_merged()
    {
        var r = InvoiceCalculator.Calculate(new[]
        {
            new InvoiceLineInput(new Quantity(1000), new Money(10000), Money.Zero, new GstRate(1800), new GstRate(0)),
            new InvoiceLineInput(new Quantity(1000), new Money(20000), Money.Zero, new GstRate(500), new GstRate(0)),
        }, TaxSplit.Igst, false, false);
        var p = GstPosting.Post(r, new[] { R(RequiredLedger.Sales), R(RequiredLedger.Sales) },
            TradeDirection.Outward, GstDocumentKind.Invoice, false, acc);
        Assert.Equal(new PostingLine(R(RequiredLedger.Sales), new Money(-30000)), p[1]);
        Assert.Equal(new PostingLine(R(RequiredLedger.OutputIgst), new Money(-2800)), p[2]);
        Assert.Equal(Money.Zero, Money.Sum(p.Select(l => l.Amount)));
    }

    [Fact]
    public void Outward_reverse_charge_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            GstPosting.Post(Sale999(), new[] { R(RequiredLedger.Sales) }, TradeDirection.Outward, GstDocumentKind.Invoice, true, acc));

    [Fact]
    public void Line_ledger_count_must_match() =>
        Assert.Throws<ArgumentException>(() =>
            GstPosting.Post(Sale999(), Array.Empty<LedgerId>(), TradeDirection.Outward, GstDocumentKind.Invoice, false, acc));

    [Fact]
    public void Reverse_charge_without_input_credit_puts_the_tax_in_the_cost()
    {
        var r = InvoiceCalculator.Calculate(
            new[] { new InvoiceLineInput(new Quantity(1000), new Money(100000), Money.Zero, new GstRate(1800), new GstRate(0)) },
            TaxSplit.CgstSgst, true, true);
        var p = GstPosting.Post(r, new[] { R(RequiredLedger.Purchases) }, TradeDirection.Inward, GstDocumentKind.Invoice, true, acc,
            inputCredit: false);
        Assert.Equal(new[]
        {
            new PostingLine(R(RequiredLedger.Purchases), new Money(118000)),
            new PostingLine(R(RequiredLedger.RcmPayableCgst), new Money(-9000)),
            new PostingLine(R(RequiredLedger.RcmPayableSgst), new Money(-9000)),
            new PostingLine(party, new Money(-100000)),
        }, p);
    }
}
