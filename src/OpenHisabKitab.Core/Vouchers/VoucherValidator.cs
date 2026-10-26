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

        var known = lines.Where(l => chart.Contains(l.Ledger)).ToList();
        if (known.Count != lines.Count) errors.Add(VoucherError.UnknownLedger);

        switch (v.Type)
        {
            case VoucherType.Contra when known.Any(l => !chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.ContraNonCashLedger);
                break;
            case VoucherType.Receipt when !known.Any(l => l.Amount.Paise > 0 && chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.ReceiptWithoutCashDebit);
                break;
            case VoucherType.Payment when !known.Any(l => l.Amount.Paise < 0 && chart.IsCashOrBank(l.Ledger)):
                errors.Add(VoucherError.PaymentWithoutCashCredit);
                break;
        }

        return errors.ToList();
    }
}
