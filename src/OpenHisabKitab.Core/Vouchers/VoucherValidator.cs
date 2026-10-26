using OpenHisabKitab.Core.Accounts;

namespace OpenHisabKitab.Core.Vouchers;

public enum VoucherError
{
    Unbalanced,
    TooFewLines,
    ZeroLine,
    UnknownLedger,
    ContraNonCashLedger,
    ReceiptWithoutCashDebit,
    PaymentWithoutCashCredit,
}

public static class VoucherValidator
{
    /// <summary>Returns every rule the voucher breaks, each once, in enum order. Empty means valid.</summary>
    public static IReadOnlyList<VoucherError> Validate(VoucherDraft v, ChartOfAccounts chart)
    {
        var errors = new SortedSet<VoucherError>();
        var lines = v.Lines;

        if (lines.Count < 2) errors.Add(VoucherError.TooFewLines);
        if (lines.Any(l => l.Amount.Paise == 0)) errors.Add(VoucherError.ZeroLine);

        // decimal holds any sum of longs here, so huge lines report Unbalanced instead of overflowing.
        if (lines.Sum(l => (decimal)l.Amount.Paise) != 0m) errors.Add(VoucherError.Unbalanced);

        if (lines.Any(l => !chart.Contains(l.Ledger))) errors.Add(VoucherError.UnknownLedger);

        // Count and type rules apply to the lines as stored: one net line per ledger (spec 5.1).
        var net = NetByLedger(lines);
        if (lines.Count >= 2 && net.Count < 2) errors.Add(VoucherError.TooFewLines);
        var known = net.Where(l => chart.Contains(l.Ledger)).ToList();

        switch (v.Type)
        {
            case VoucherType.Contra when known.Any(l => !chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.ContraNonCashLedger);
                break;
            case VoucherType.Receipt when !known.Any(l => l.Amount > 0 && chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.ReceiptWithoutCashDebit);
                break;
            case VoucherType.Payment when !known.Any(l => l.Amount < 0 && chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.PaymentWithoutCashCredit);
                break;
        }

        return errors.ToList();
    }

    // Same result as Postings.Consolidate, but summed in decimal so validation never throws on overflow.
    static List<(LedgerId Ledger, decimal Amount)> NetByLedger(IEnumerable<PostingLine> lines) =>
        lines.GroupBy(l => l.Ledger)
            .Select(g => (g.Key, g.Sum(l => (decimal)l.Amount.Paise)))
            .Where(x => x.Item2 != 0m)
            .ToList();
}
