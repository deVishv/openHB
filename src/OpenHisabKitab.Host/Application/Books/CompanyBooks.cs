using System.Collections.Concurrent;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Each company's posting lines held in memory, so reports never re-read the database (spec 5.1, 10). A saved voucher
/// replaces its own lines; the report engine is rebuilt from the held lines on the next report after a change.
/// The rebuild runs outside the company's lock, so a save never waits for it.
/// </summary>
public sealed class CompanyBooks(CompanyHub hub) : IBooksObserver
{
    sealed class State
    {
        public bool Loaded;
        public long Version;
        public ChartOfAccounts Chart = ChartOfAccounts.Empty;
        public DateOnly BooksStart;
        public IReadOnlyDictionary<LedgerId, Money> Openings = new Dictionary<LedgerId, Money>();
        public Dictionary<long, PostingRecord[]> ByVoucher = [];
        public Books? Built;
        public long BuiltVersion = -1;
    }

    readonly ConcurrentDictionary<int, State> states = new();

    /// <summary>Test hook: runs when a report build starts, outside the company's lock.</summary>
    internal Action? BuildStarted { get; set; }

    public Books Get(int company) => GetWithChart(company).Books;

    /// <summary>The report engine with the chart it was built from, read together so they always match.</summary>
    public (Books Books, ChartOfAccounts Chart) GetWithChart(int company)
    {
        var s = states.GetOrAdd(company, _ => new State());
        long version;
        ChartOfAccounts chart;
        DateOnly start;
        IReadOnlyDictionary<LedgerId, Money> openings;
        PostingRecord[][] lines;
        lock (s)
        {
            if (!s.Loaded) Load(company, s);
            if (s.Built is { } built && s.BuiltVersion == s.Version) return (built, s.Chart);
            (version, chart, start, openings) = (s.Version, s.Chart, s.BooksStart, s.Openings);
            lines = [.. s.ByVoucher.Values];
        }

        BuildStarted?.Invoke();
        var books = new Books(chart, start, openings, lines.SelectMany(p => p));
        lock (s)
        {
            // A change during the build leaves this result out of the cache, but it is still a consistent snapshot to return.
            if (s.Version == version)
            {
                s.Built = books;
                s.BuiltVersion = version;
            }
        }
        return (books, chart);
    }

    public void VoucherSaved(int company, long voucherId, IReadOnlyList<PostingRecord> postings)
    {
        if (!states.TryGetValue(company, out var s)) return;
        lock (s)
        {
            // Not loaded yet: the next load reads the committed lines from the database.
            if (!s.Loaded) return;
            if (postings.Count == 0) s.ByVoucher.Remove(voucherId);
            else s.ByVoucher[voucherId] = [.. postings];
            s.Version++;
        }
    }

    public void ChartChanged(int company) => Invalidate(company);

    /// <summary>Drops everything held for the company; the next report reloads it from the database.</summary>
    public void Invalidate(int company)
    {
        if (!states.TryGetValue(company, out var s)) return;
        lock (s)
        {
            s.Loaded = false;
            s.Built = null;
            s.ByVoucher = [];
            s.Version++;
        }
    }

    /// <summary>Test hook: holds the given books without a database.</summary>
    internal void Seed(int company, ChartOfAccounts chart, DateOnly booksStart, IReadOnlyDictionary<LedgerId, Money> openings,
        IEnumerable<PostingRecord> postings)
    {
        var s = states.GetOrAdd(company, _ => new State());
        lock (s)
        {
            s.Chart = chart;
            s.BooksStart = booksStart;
            s.Openings = openings;
            s.ByVoucher = postings.GroupBy(p => p.VoucherId).ToDictionary(g => g.Key, g => g.ToArray());
            s.Built = null;
            s.Version++;
            s.Loaded = true;
        }
    }

    // Reads chart, openings, and postings in one read transaction, so they come from the same committed state.
    void Load(int company, State s)
    {
        using var c = hub.Open(company);
        using var snapshot = c.BeginTransaction(deferred: true);
        s.Chart = ChartRepository.Load(c);
        s.BooksStart = new FinancialYear(CompanyRepository.GetProfile(c).FirstFyStartYear).Start;
        s.Openings = ChartRepository.Openings(c);
        s.ByVoucher = VoucherRepository.AllPostings(c).GroupBy(p => p.VoucherId).ToDictionary(g => g.Key, g => g.ToArray());
        snapshot.Commit();
        s.Built = null;
        s.Version++;
        s.Loaded = true;
    }
}
