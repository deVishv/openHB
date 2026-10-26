namespace OpenHisabKitab.Core.Tests.Vouchers;

public class VoucherValidatorTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();
    LedgerId Cash => c.Required(RequiredLedger.Cash);
    LedgerId Sales => c.Required(RequiredLedger.Sales);
    LedgerId Capital => c.Required(RequiredLedger.Capital);
    static readonly DateOnly D = new(2026, 10, 26);

    static VoucherDraft V(VoucherType t, params (LedgerId l, long p)[] lines) =>
        new(t, D, lines.Select(x => new PostingLine(x.l, new Money(x.p))).ToList());

    [Fact]
    public void Balanced_journal_is_valid() =>
        Assert.Empty(VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100), (Capital, -100)), c));

    [Fact]
    public void Unbalanced() =>
        Assert.Equal(new[] { VoucherError.Unbalanced },
            VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100), (Capital, -99)), c));

    [Fact]
    public void Single_line() =>
        Assert.Contains(VoucherError.TooFewLines, VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 100)), c));

    [Fact]
    public void Zero_line() =>
        Assert.Contains(VoucherError.ZeroLine, VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 0), (Capital, 0)), c));

    [Fact]
    public void Unknown_ledger() =>
        Assert.Contains(VoucherError.UnknownLedger,
            VoucherValidator.Validate(V(VoucherType.Journal, (new LedgerId(9999), 1), (Cash, -1)), c));

    [Fact]
    public void Contra_rejects_non_cash() =>
        Assert.Contains(VoucherError.ContraNonCashLedger,
            VoucherValidator.Validate(V(VoucherType.Contra, (Cash, 1), (Sales, -1)), c));

    [Fact]
    public void Receipt_needs_cash_debit() =>
        Assert.Contains(VoucherError.ReceiptWithoutCashDebit,
            VoucherValidator.Validate(V(VoucherType.Receipt, (Sales, 1), (Cash, -1)), c));

    [Fact]
    public void Payment_needs_cash_credit() =>
        Assert.Contains(VoucherError.PaymentWithoutCashCredit,
            VoucherValidator.Validate(V(VoucherType.Payment, (Cash, 500), (Capital, -500)), c));

    [Fact]
    public void Payment_with_cash_credit_is_valid() =>
        Assert.Empty(VoucherValidator.Validate(V(VoucherType.Payment, (Capital, 500), (Cash, -500)), c));

    [Fact]
    public void Each_error_is_reported_once() =>
        Assert.Equal(new[] { VoucherError.ZeroLine },
            VoucherValidator.Validate(V(VoucherType.Journal, (Cash, 0), (Capital, 0), (Sales, 0)), c));

    [Fact]
    public void Huge_lines_report_unbalanced_instead_of_crashing() =>
        Assert.Contains(VoucherError.Unbalanced,
            VoucherValidator.Validate(V(VoucherType.Journal, (Cash, long.MaxValue), (Capital, long.MaxValue)), c));

    [Fact]
    public void Consolidate_merges_and_drops_zero()
    {
        var r = Postings.Consolidate(new[]
        {
            new PostingLine(Cash, new Money(100)), new PostingLine(Sales, new Money(-150)),
            new PostingLine(Cash, new Money(50)), new PostingLine(Capital, new Money(10)),
            new PostingLine(Capital, new Money(-10)),
        });
        Assert.Equal(new[] { new PostingLine(Cash, new Money(150)), new PostingLine(Sales, new Money(-150)) }, r);
    }
}
