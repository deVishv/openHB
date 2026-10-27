namespace OpenHisabKitab.Core.Tests.Fixtures;

/// <summary>
/// A small company whose report figures were worked out by hand during planning.
/// Books start 1-4-2025: Cash 50,000 Dr, Capital 50,000 Cr. Intra state 18%, round off on.
/// </summary>
public static class GoldenCompany
{
    public static (Books Books, ChartOfAccounts Chart, IReadOnlyDictionary<string, LedgerId> Ledgers) Build()
    {
        var chart = DefaultChart.Create();
        (chart, var gupta) = chart.AddLedger("Gupta Enterprises", chart.Group(GroupKind.TradeReceivables));
        (chart, var mehta) = chart.AddLedger("Mehta and Sons", chart.Group(GroupKind.TradePayables));
        (chart, var rent) = chart.AddLedger("Rent", chart.Group(GroupKind.OtherExpenses));
        (chart, var interest) = chart.AddLedger("Interest Received", chart.Group(GroupKind.OtherIncome));

        var cash = chart.Required(RequiredLedger.Cash);
        var capital = chart.Required(RequiredLedger.Capital);
        var ledgers = new Dictionary<string, LedgerId>
        {
            ["Gupta"] = gupta, ["Mehta"] = mehta, ["Rent"] = rent, ["Interest"] = interest, ["Cash"] = cash,
            ["Capital"] = capital,
            ["Sales"] = chart.Required(RequiredLedger.Sales), ["Purchases"] = chart.Required(RequiredLedger.Purchases),
            ["OutputCgst"] = chart.Required(RequiredLedger.OutputCgst), ["OutputSgst"] = chart.Required(RequiredLedger.OutputSgst),
            ["InputCgst"] = chart.Required(RequiredLedger.InputCgst), ["InputSgst"] = chart.Required(RequiredLedger.InputSgst),
            ["ProfitAndLoss"] = chart.Required(RequiredLedger.ProfitAndLoss),
            ["RoundOff"] = chart.Required(RequiredLedger.RoundOff),
            ["OutputUtgst"] = chart.Required(RequiredLedger.OutputUtgst), ["OutputIgst"] = chart.Required(RequiredLedger.OutputIgst),
            ["InputIgst"] = chart.Required(RequiredLedger.InputIgst), ["RcmIgst"] = chart.Required(RequiredLedger.RcmPayableIgst),
        };

        var postings = new List<PostingRecord>();
        void Add(long id, DateOnly date, IEnumerable<PostingLine> lines) =>
            postings.AddRange(lines.Select(l => new PostingRecord(id, date, l.Ledger, l.Amount)));
        static PostingLine[] Pair(LedgerId dr, LedgerId cr, long rupees) =>
            [new(dr, Money.FromRupees(rupees)), new(cr, -Money.FromRupees(rupees))];
        static InvoiceResult Invoice(long rupees) => InvoiceCalculator.Calculate(
            [new InvoiceLineInput(new Quantity(1000), Money.FromRupees(rupees), Money.Zero, new GstRate(1800), new GstRate(0))],
            TaxSplit.CgstSgst, roundOff: true, reverseCharge: false);

        Add(1, new(2025, 4, 10), GstPosting.Post(Invoice(10000), [ledgers["Sales"]],
            TradeDirection.Outward, GstDocumentKind.Invoice, false, GstPostingAccounts.FromChart(chart, gupta)));
        Add(2, new(2025, 4, 15), GstPosting.Post(Invoice(6000), [ledgers["Purchases"]],
            TradeDirection.Inward, GstDocumentKind.Invoice, false, GstPostingAccounts.FromChart(chart, mehta)));
        Add(3, new(2025, 4, 20), Pair(cash, gupta, 11800));
        Add(4, new(2025, 4, 25), Pair(rent, cash, 2000));
        Add(5, new(2025, 4, 30), Pair(mehta, cash, 7080));
        Add(6, new(2026, 5, 5), Pair(cash, interest, 1000));

        static InvoiceResult Priced(long paise, int rate, TaxSplit split, bool reverseCharge = false) => InvoiceCalculator.Calculate(
            [new InvoiceLineInput(new Quantity(1000), new Money(paise), Money.Zero, new GstRate(rate), new GstRate(0))],
            split, roundOff: true, reverseCharge);
        var toGupta = GstPostingAccounts.FromChart(chart, gupta);

        // V7: inter state sale 999.99 at 18% IGST; tax 180.00, total 1179.99 rounds to 1180.00.
        Add(7, new(2026, 6, 10), GstPosting.Post(Priced(99999, 1800, TaxSplit.Igst), [ledgers["Sales"]],
            TradeDirection.Outward, GstDocumentKind.Invoice, false, toGupta));
        // V8: credit note 500.00 at 18%; CGST 45.00 and SGST 45.00 reversed.
        Add(8, new(2026, 6, 15), GstPosting.Post(Priced(50000, 1800, TaxSplit.CgstSgst), [ledgers["Sales"]],
            TradeDirection.Outward, GstDocumentKind.CreditNote, false, toGupta));
        // V9: reverse charge purchase 2,000.00 at 18% IGST; Mehta is owed 2,000.00 and 360.00 is RCM payable.
        Add(9, new(2026, 6, 20), GstPosting.Post(Priced(200000, 1800, TaxSplit.Igst, reverseCharge: true), [ledgers["Purchases"]],
            TradeDirection.Inward, GstDocumentKind.Invoice, true, GstPostingAccounts.FromChart(chart, mehta)));
        // V10: UTGST sale 100.05 at 5%; CGST 2.50 and UTGST 2.50, total 105.05 rounds to 105.00.
        Add(10, new(2026, 6, 25), GstPosting.Post(Priced(10005, 500, TaxSplit.CgstUtgst), [ledgers["Sales"]],
            TradeDirection.Outward, GstDocumentKind.Invoice, false, toGupta));

        var openings = new Dictionary<LedgerId, Money> { [cash] = Money.FromRupees(50000), [capital] = -Money.FromRupees(50000) };
        return (new Books(chart, new(2025, 4, 1), openings, postings), chart, ledgers);
    }
}
