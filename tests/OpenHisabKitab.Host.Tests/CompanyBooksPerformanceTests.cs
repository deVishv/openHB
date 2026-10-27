using System.Diagnostics;
using Xunit.Abstractions;

namespace OpenHisabKitab.Host.Tests;

public sealed class CompanyBooksPerformanceTests(ITestOutputHelper output) : IDisposable
{
    readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    // Spec 10: TRLB for a full year under 1 s with 1,000,000 posting lines. Plan 1 measured the engine build at about 0.55 s.
    [Fact]
    [Trait("Category", "Performance")]
    public void Full_year_trial_balance_after_a_save_is_under_one_second_at_a_million_lines()
    {
        var chart = DefaultChart.Create();
        var ledgers = new List<LedgerId>();
        for (var i = 0; i < 200; i++)
        {
            var (next, id) = chart.AddLedger($"Ledger {i}", chart.Group(GroupKind.CashInHand));
            chart = next;
            ledgers.Add(id);
        }
        var r = new Random(1);
        var fy = new FinancialYear(2026);
        var postings = new List<PostingRecord>(1_000_000);
        for (long v = 1; v <= 500_000; v++)
        {
            var date = fy.Start.AddDays(r.Next(365));
            var amount = new Money(r.Next(1, 1_000_000));
            var a = ledgers[r.Next(ledgers.Count)];
            var b = ledgers[r.Next(ledgers.Count)];
            postings.Add(new PostingRecord(v, date, a, amount));
            postings.Add(new PostingRecord(v, date, b, -amount));
        }
        const int company = 1;
        host.Books.Seed(company, chart, fy.Start, new Dictionary<LedgerId, Money>(), postings);

        var best = TimeSpan.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var amount = new Money(100 + attempt);
            host.Books.VoucherSaved(company, 500_001, [new PostingRecord(500_001, fy.Start, ledgers[0], amount), new PostingRecord(500_001, fy.Start, ledgers[1], -amount)]);
            var watch = Stopwatch.StartNew();
            var tb = host.Books.Get(company).TrialBalance(fy.Range);
            watch.Stop();
            Assert.Equal(tb.TotalDebit, tb.TotalCredit);
            if (watch.Elapsed < best) best = watch.Elapsed;
        }
        output.WriteLine($"Best of 3: {best.TotalMilliseconds:F0} ms");
        Assert.True(best < TimeSpan.FromSeconds(1), $"Best of 3 was {best.TotalMilliseconds:F0} ms.");
    }
}
