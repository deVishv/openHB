namespace OpenHisabKitab.Core.Tests.Reports;

public class TrialBalanceTests
{
    readonly BooksFixture f = new();
    static readonly FinancialYear Fy25 = new(2025);
    static readonly FinancialYear Fy26 = new(2026);

    static Money M(long paise) => new(paise);

    TrialBalanceRow Row(TrialBalance tb, LedgerId l) => tb.Rows.Single(r => r.Ledger == l);

    [Fact]
    public void Trial_balance_fy_2025_totals()
    {
        var tb = f.Books.TrialBalance(Fy25.Range);
        Assert.Equal(M(6000000), tb.TotalDebit);
        Assert.Equal(M(6000000), tb.TotalCredit);
        Assert.Equal(new TrialBalanceRow(f.Cash, M(5000000), M(1000000), M(200000), M(5800000)), Row(tb, f.Cash));
    }

    [Fact]
    public void Trial_balance_fy_2025_rows_in_chart_order()
    {
        var tb = f.Books.TrialBalance(Fy25.Range);
        Assert.Equal(new[] { f.Cash, f.Capital, f.Sales, f.Rent }, tb.Rows.Select(r => r.Ledger));
        Assert.Equal(M(-5000000), Row(tb, f.Capital).Closing);
        Assert.Equal(M(-1000000), Row(tb, f.Sales).Closing);
        Assert.Equal(M(1000000), Row(tb, f.Sales).Credits);
        Assert.Equal(M(200000), Row(tb, f.Rent).Closing);
        Assert.Equal(Money.Zero, tb.OpeningDifference);
    }

    [Fact]
    public void Trial_balance_fy_2026_carries_profit_forward()
    {
        var tb = f.Books.TrialBalance(Fy26.Range);
        Assert.Equal(M(5800000), Row(tb, f.Cash).Opening);
        Assert.Equal(Money.Zero, Row(tb, f.Sales).Opening);
        Assert.Equal(M(-300000), Row(tb, f.Sales).Closing);
        Assert.DoesNotContain(tb.Rows, r => r.Ledger == f.Rent);
        Assert.Equal(M(-800000), Row(tb, f.ProfitAndLoss).Opening);
        Assert.Equal(M(-800000), Row(tb, f.ProfitAndLoss).Closing);
        Assert.Equal(M(6100000), tb.TotalDebit);
        Assert.Equal(M(6100000), tb.TotalCredit);
    }

    [Fact]
    public void Sub_period_opening_includes_earlier_income_and_expense_of_same_year()
    {
        var tb = f.Books.TrialBalance(new DateRange(new(2025, 5, 1), new(2026, 3, 31)));
        Assert.Equal(new TrialBalanceRow(f.Rent, M(200000), Money.Zero, Money.Zero, M(200000)), Row(tb, f.Rent));
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
    }

    [Fact]
    public void Opening_difference_keeps_totals_equal()
    {
        var g = new BooksFixture(capitalOpeningPaise: -4999000);
        Assert.Equal(M(1000), g.Books.OpeningDifference);
        var tb = g.Books.TrialBalance(Fy25.Range);
        Assert.Equal(M(1000), tb.OpeningDifference);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
        Assert.Equal(M(6000000), tb.TotalDebit);
    }

    [Fact] // Review Focus 5
    public void Period_crossing_31_march_throws() =>
        Assert.Throws<ArgumentException>(() => f.Books.TrialBalance(new DateRange(new(2026, 3, 1), new(2026, 4, 30))));

    [Fact]
    public void Posting_before_books_start_throws() =>
        Assert.Throws<ArgumentException>(() => new Books(f.Chart, BooksFixture.BooksStart, f.Openings,
            [.. f.Postings, new PostingRecord(9, new(2025, 3, 31), f.Cash, M(1))]));

    [Fact]
    public void Opening_on_income_ledger_throws() =>
        Assert.Throws<ArgumentException>(() => new Books(f.Chart, BooksFixture.BooksStart,
            new Dictionary<LedgerId, Money> { [f.Sales] = M(1) }, f.Postings));

    [Fact]
    public void Books_must_start_on_1_april() =>
        Assert.Throws<ArgumentException>(() => new Books(f.Chart, new(2025, 4, 2), f.Openings, []));

    [Fact]
    public void Unknown_ledger_throws()
    {
        Assert.Throws<ArgumentException>(() => new Books(f.Chart, BooksFixture.BooksStart, f.Openings,
            [new PostingRecord(9, new(2025, 5, 1), new LedgerId(9999), M(1))]));
        Assert.Throws<ArgumentException>(() => new Books(f.Chart, BooksFixture.BooksStart,
            new Dictionary<LedgerId, Money> { [new LedgerId(9999)] = M(1) }, []));
    }
}
