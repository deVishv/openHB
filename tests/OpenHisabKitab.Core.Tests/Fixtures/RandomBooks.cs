namespace OpenHisabKitab.Core.Tests.Fixtures;

public sealed record GeneratedInvoice(InvoiceResult Result, TradeDirection Direction, GstDocumentKind Kind, bool ReverseCharge);

public sealed record GeneratedVoucher(long Id, DateOnly Date, IReadOnlyList<PostingLine> Lines, GeneratedInvoice? Invoice);

/// <summary>Generates a reproducible random company across FY 2025-26 and FY 2026-27 from a seed.</summary>
public static class RandomBooks
{
    public static readonly DateOnly BooksStart = new(2025, 4, 1);
    const int VoucherCount = 300;
    static readonly int[] Rates = [0, 500, 1800, 2800];

    public static (Books Books, ChartOfAccounts Chart, IReadOnlyList<GeneratedVoucher> Vouchers) Generate(int seed)
    {
        var rng = new Random(seed);
        var chart = DefaultChart.Create();
        LedgerId Add(string name, GroupKind kind)
        {
            (chart, var id) = chart.AddLedger(name, chart.Group(kind));
            return id;
        }

        LedgerId[] receivables = [Add("Customer A", GroupKind.TradeReceivables), Add("Customer B", GroupKind.TradeReceivables), Add("Customer C", GroupKind.TradeReceivables)];
        LedgerId[] payables = [Add("Supplier A", GroupKind.TradePayables), Add("Supplier B", GroupKind.TradePayables)];
        var bank = Add("HDFC Bank", GroupKind.BankAccounts);
        var rent = Add("Rent", GroupKind.OtherExpenses);
        var interest = Add("Interest Received", GroupKind.OtherIncome);
        var cash = chart.Required(RequiredLedger.Cash);

        var openings = new Dictionary<LedgerId, Money>();
        foreach (var l in new[] { cash, bank }.Concat(receivables)) openings[l] = new Money(rng.NextInt64(0, 100_000_001));
        foreach (var l in payables) openings[l] = -new Money(rng.NextInt64(0, 100_000_001));
        // Every fourth seed leaves an opening difference so reports are tested with unbalanced openings too.
        var difference = seed % 4 == 0 ? new Money(rng.NextInt64(-1_000_000, 1_000_001)) : Money.Zero;
        openings[chart.Required(RequiredLedger.Capital)] = difference - Money.Sum(openings.Values);

        var allLedgers = chart.Ledgers.Select(l => l.Id).ToArray();
        var days = new FinancialYear(2026).End.DayNumber - BooksStart.DayNumber + 1;
        var vouchers = new List<GeneratedVoucher>();
        for (var id = 1L; vouchers.Count < VoucherCount; id++)
        {
            var date = BooksStart.AddDays(rng.Next(days));
            var voucher = rng.NextDouble() < 0.6
                ? Invoice(rng, id, date, chart, receivables, payables, rent, interest)
                : Journal(rng, id, date, allLedgers);
            if (voucher.Lines.Count >= 2) vouchers.Add(voucher);
        }

        var postings = vouchers.SelectMany(v => v.Lines.Select(l => new PostingRecord(v.Id, v.Date, l.Ledger, l.Amount)));
        return (new Books(chart, BooksStart, openings, postings), chart, vouchers);
    }

    static GeneratedVoucher Invoice(Random rng, long id, DateOnly date, ChartOfAccounts chart,
        LedgerId[] receivables, LedgerId[] payables, LedgerId rent, LedgerId interest)
    {
        var direction = rng.Next(2) == 0 ? TradeDirection.Outward : TradeDirection.Inward;
        var kind = (GstDocumentKind)rng.Next(3);
        var split = (TaxSplit)rng.Next(3);
        var reverseCharge = direction == TradeDirection.Inward && rng.Next(5) == 0;
        var roundOff = rng.Next(2) == 0;

        var inputs = new List<InvoiceLineInput>();
        var lineLedgers = new List<LedgerId>();
        for (var i = rng.Next(1, 5); i > 0; i--)
        {
            var qty = new Quantity(rng.NextInt64(1, 100_001));
            var price = new Money(rng.NextInt64(1, 10_000_001));
            var gross = (long)qty.Times(price);
            var discount = rng.Next(5) == 0 ? new Money(rng.NextInt64(0, gross / 10 + 1)) : Money.Zero;
            inputs.Add(new InvoiceLineInput(qty, price, discount, new GstRate(Rates[rng.Next(Rates.Length)]), new GstRate(0),
                PriceIncludesTax: rng.Next(4) == 0));
            lineLedgers.Add(direction == TradeDirection.Outward
                ? (rng.Next(4) == 0 ? interest : chart.Required(RequiredLedger.Sales))
                : (rng.Next(4) == 0 ? rent : chart.Required(RequiredLedger.Purchases)));
        }

        var party = direction == TradeDirection.Outward ? receivables[rng.Next(receivables.Length)] : payables[rng.Next(payables.Length)];
        var result = InvoiceCalculator.Calculate(inputs, split, roundOff, reverseCharge);
        var lines = GstPosting.Post(result, lineLedgers, direction, kind, reverseCharge, GstPostingAccounts.FromChart(chart, party));
        return new GeneratedVoucher(id, date, lines, new GeneratedInvoice(result, direction, kind, reverseCharge));
    }

    static GeneratedVoucher Journal(Random rng, long id, DateOnly date, LedgerId[] ledgers)
    {
        var lines = new List<PostingLine>();
        for (var i = rng.Next(1, 5); i > 0; i--)
        {
            var amount = rng.NextInt64(1, 100_000_001) * (rng.Next(2) == 0 ? 1 : -1);
            lines.Add(new PostingLine(ledgers[rng.Next(ledgers.Length)], new Money(amount)));
        }
        lines.Add(new PostingLine(ledgers[rng.Next(ledgers.Length)], -Money.Sum(lines.Select(l => l.Amount))));
        return new GeneratedVoucher(id, date, Postings.Consolidate(lines), null);
    }
}
