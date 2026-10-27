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
/// </summary>
public sealed class CompanyBooks(CompanyHub hub) : IBooksObserver
{
    sealed class State
    {
        public bool Loaded;
        public ChartOfAccounts Chart = ChartOfAccounts.Empty;
        public DateOnly BooksStart;
        public IReadOnlyDictionary<LedgerId, Money> Openings = new Dictionary<LedgerId, Money>();
        public Dictionary<long, PostingRecord[]> ByVoucher = [];
        public Books? Built;
    }

    readonly ConcurrentDictionary<int, State> states = new();

    public Books Get(int company) => GetWithChart(company).Books;

    /// <summary>The report engine with the chart it was built from, read together so they always match.</summary>
    public (Books Books, ChartOfAccounts Chart) GetWithChart(int company)
    {
        var s = states.GetOrAdd(company, _ => new State());
        lock (s)
        {
            if (!s.Loaded) Load(company, s);
            s.Built ??= new Books(s.Chart, s.BooksStart, s.Openings, s.ByVoucher.Values.SelectMany(p => p));
            return (s.Built, s.Chart);
        }
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
            s.Built = null;
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
            s.Loaded = true;
        }
    }

    void Load(int company, State s)
    {
        using var c = hub.Open(company);
        s.Chart = ChartRepository.Load(c);
        s.BooksStart = new FinancialYear(CompanyRepository.GetProfile(c).FirstFyStartYear).Start;
        s.Openings = ChartRepository.Openings(c);
        s.ByVoucher = VoucherRepository.AllPostings(c).GroupBy(p => p.VoucherId).ToDictionary(g => g.Key, g => g.ToArray());
        s.Built = null;
        s.Loaded = true;
    }
}
