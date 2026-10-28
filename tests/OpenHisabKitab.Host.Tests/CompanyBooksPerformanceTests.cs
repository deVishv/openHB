using System.Diagnostics;
using Xunit.Abstractions;

namespace OpenHisabKitab.Host.Tests;

// Timings run alone, after the parallel tests, so other tests' CPU use does not count against the target.
[CollectionDefinition(nameof(CompanyBooksPerformanceTests), DisableParallelization = true)]
public sealed class PerformanceCollection;

[Collection(nameof(CompanyBooksPerformanceTests))]
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

    [Fact]
    [Trait("Category", "Performance")]
    public async Task A_save_does_not_wait_for_a_report_build()
    {
        var chart = DefaultChart.Create();
        var cash = chart.Required(RequiredLedger.Cash);
        var sales = chart.Required(RequiredLedger.Sales);
        var fy = new FinancialYear(2026);
        var postings = new List<PostingRecord>(1_000_000);
        for (long v = 1; v <= 500_000; v++)
        {
            postings.Add(new PostingRecord(v, fy.Start, cash, new Money(100)));
            postings.Add(new PostingRecord(v, fy.Start, sales, new Money(-100)));
        }
        host.Books.Seed(1, chart, fy.Start, new Dictionary<LedgerId, Money>(), postings);
        using var building = new ManualResetEventSlim();
        host.Books.BuildStarted = () => building.Set();

        var report = Task.Run(() => host.Books.Get(1));
        Assert.True(building.Wait(TimeSpan.FromSeconds(10)));
        var watch = Stopwatch.StartNew();
        host.Books.VoucherSaved(1, 500_001,
            [new PostingRecord(500_001, fy.Start, cash, new Money(7)), new PostingRecord(500_001, fy.Start, sales, new Money(-7))]);
        watch.Stop();
        await report;
        host.Books.BuildStarted = null;

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(100), $"Save waited {watch.Elapsed.TotalMilliseconds:F0} ms.");
        var tb = host.Books.Get(1).TrialBalance(fy.Range);
        Assert.Equal(new Money(50_000_007), tb.Rows.Single(r => r.Ledger == cash).Debits);
    }
}
