namespace OpenHisabKitab.Core.Tests.Reports;

public class LedgerStatementTests
{
    readonly BooksFixture f = new();

    static Money M(long paise) => new(paise);

    [Fact]
    public void Cash_statement_fy_2025()
    {
        var s = f.Books.Statement(f.Cash, new FinancialYear(2025).Range);
        Assert.Equal(M(5000000), s.Opening);
        Assert.Equal(new[]
        {
            new StatementRow(new(2025, 4, 10), 1, M(1000000), M(6000000)),
            new StatementRow(new(2025, 4, 20), 2, M(-200000), M(5800000)),
        }, s.Rows);
        Assert.Equal(M(5800000), s.Closing);
    }

    [Fact]
    public void Rows_on_same_date_are_ordered_by_voucher_id()
    {
        var postings = new List<PostingRecord>
        {
            new(7, new(2025, 6, 1), f.Cash, M(100)), new(7, new(2025, 6, 1), f.Capital, M(-100)),
            new(5, new(2025, 6, 1), f.Cash, M(200)), new(5, new(2025, 6, 1), f.Capital, M(-200)),
        };
        var books = new Books(f.Chart, BooksFixture.BooksStart, f.Openings, postings);
        var s = books.Statement(f.Cash, new FinancialYear(2025).Range);
        Assert.Equal(new long[] { 5, 7 }, s.Rows.Select(r => r.VoucherId));
    }

    [Fact]
    public void Statement_period_crossing_31_march_throws() =>
        Assert.Throws<ArgumentException>(() => f.Books.Statement(f.Cash, new DateRange(new(2026, 3, 1), new(2026, 4, 30))));
}
