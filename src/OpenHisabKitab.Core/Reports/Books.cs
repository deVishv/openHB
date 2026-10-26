using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Reports;

/// <summary>
/// The single source of every report figure (spec 5.1). Postings are indexed once by ledger with
/// running totals, so each balance is a binary search and each report reads each ledger once.
/// </summary>
public sealed class Books
{
    readonly ChartOfAccounts chart;
    readonly IReadOnlyDictionary<LedgerId, Money> openings;
    readonly Dictionary<LedgerId, LedgerIndex> index;
    readonly LedgerId profitAndLoss;
    readonly List<LedgerId> incomeAndExpense;

    public Books(ChartOfAccounts chart, DateOnly booksStart, IReadOnlyDictionary<LedgerId, Money> openingBalances, IEnumerable<PostingRecord> postings)
    {
        if (booksStart.Month != 4 || booksStart.Day != 1)
            throw new ArgumentException("Books must start on 1 April.", nameof(booksStart));

        foreach (var (ledger, _) in openingBalances)
        {
            if (!chart.Contains(ledger)) throw new ArgumentException($"Opening balance for unknown ledger {ledger.Value}.");
            if (IsIncomeOrExpense(chart, ledger))
                throw new ArgumentException($"Opening balance given for income or expense ledger {chart.Ledger(ledger).Name}.");
        }

        var list = postings.ToList();
        foreach (var p in list)
        {
            if (!chart.Contains(p.Ledger)) throw new ArgumentException($"Posting for unknown ledger {p.Ledger.Value}.");
            if (p.Date < booksStart) throw new ArgumentException($"Posting dated {p.Date:dd-MM-yyyy} is before the books start.");
        }

        this.chart = chart;
        openings = openingBalances;
        OpeningDifference = Money.Sum(openingBalances.Values);
        profitAndLoss = chart.Required(RequiredLedger.ProfitAndLoss);
        incomeAndExpense = chart.Ledgers.Where(l => IsIncomeOrExpense(chart, l.Id)).Select(l => l.Id).ToList();
        index = list.GroupBy(p => p.Ledger).ToDictionary(g => g.Key, g => new LedgerIndex(g));
    }

    /// <summary>Sum of all opening balances. Zero when the openings balance.</summary>
    public Money OpeningDifference { get; }

    public TrialBalance TrialBalance(DateRange period)
    {
        RequireSingleYear(period);
        var rows = new List<TrialBalanceRow>();
        foreach (var ledger in chart.Ledgers)
        {
            var row = Activity(ledger.Id, period);
            if (row.Opening != Money.Zero || row.Debits != Money.Zero || row.Credits != Money.Zero || row.Closing != Money.Zero)
                rows.Add(row);
        }

        var debit = Money.Sum(rows.Where(r => r.Closing > Money.Zero).Select(r => r.Closing));
        var credit = -Money.Sum(rows.Where(r => r.Closing < Money.Zero).Select(r => r.Closing));
        if (OpeningDifference > Money.Zero) credit += OpeningDifference;
        else debit -= OpeningDifference;
        return new TrialBalance(period, rows, OpeningDifference, debit, credit);
    }

    public ProfitAndLoss ProfitAndLoss(DateRange period)
    {
        RequireSingleYear(period);
        var income = new List<ReportRow>();
        var expenses = new List<ReportRow>();
        foreach (var ledger in incomeAndExpense)
        {
            var row = Activity(ledger, period);
            var movement = row.Debits - row.Credits;
            if (movement == Money.Zero) continue;
            if (chart.NatureOf(ledger) == AccountNature.Income) income.Add(new ReportRow(ledger, -movement));
            else expenses.Add(new ReportRow(ledger, movement));
        }

        var totalIncome = Money.Sum(income.Select(r => r.Amount));
        var totalExpenses = Money.Sum(expenses.Select(r => r.Amount));
        return new ProfitAndLoss(period, income, expenses, totalIncome, totalExpenses, totalIncome - totalExpenses);
    }

    public BalanceSheet BalanceSheet(DateOnly asAt)
    {
        var period = new DateRange(FinancialYear.Containing(asAt).Start, asAt);
        var assets = new List<ReportRow>();
        var liabilities = new List<ReportRow>();
        foreach (var ledger in chart.Ledgers)
        {
            var nature = chart.NatureOf(ledger.Id);
            if (nature is AccountNature.Income or AccountNature.Expense) continue;
            var closing = Activity(ledger.Id, period).Closing;
            if (closing == Money.Zero) continue;
            if (nature == AccountNature.Asset) assets.Add(new ReportRow(ledger.Id, closing));
            else liabilities.Add(new ReportRow(ledger.Id, -closing));
        }

        var currentProfit = ProfitAndLoss(period).NetProfit;
        var totalAssets = Money.Sum(assets.Select(r => r.Amount));
        var totalLiabilities = Money.Sum(liabilities.Select(r => r.Amount)) + currentProfit + OpeningDifference;
        return new BalanceSheet(asAt, assets, liabilities, currentProfit, OpeningDifference, totalAssets, totalLiabilities);
    }

    public LedgerStatement Statement(LedgerId ledger, DateRange period)
    {
        RequireSingleYear(period);
        if (!chart.Contains(ledger)) throw new ArgumentException($"Unknown ledger {ledger.Value}.", nameof(ledger));
        var opening = Activity(ledger, period).Opening;
        var balance = opening;
        var rows = new List<StatementRow>();
        if (index.TryGetValue(ledger, out var idx))
        {
            foreach (var p in idx.Between(period.From, period.To))
            {
                balance += p.Amount;
                rows.Add(new StatementRow(p.Date, p.VoucherId, p.Amount, balance));
            }
        }
        return new LedgerStatement(ledger, period, opening, rows, balance);
    }

    /// <summary>Opening, period debits and credits, and closing for one ledger. The period must sit in one year.</summary>
    internal TrialBalanceRow Activity(LedgerId ledger, DateRange period)
    {
        var fy = FinancialYear.Containing(period.From);
        Money opening;
        if (IsIncomeOrExpense(chart, ledger))
        {
            // Income and expense ledgers restart at zero each financial year.
            opening = SumBefore(ledger, period.From) - SumBefore(ledger, fy.Start);
        }
        else
        {
            opening = openings.GetValueOrDefault(ledger) + SumBefore(ledger, period.From);
            // Earlier years' profit or loss is carried into the Profit and Loss ledger (invariant 6).
            if (ledger == profitAndLoss)
                opening += Money.Sum(incomeAndExpense.Select(l => SumBefore(l, fy.Start)));
        }

        var debits = Money.Zero;
        var credits = Money.Zero;
        if (index.TryGetValue(ledger, out var idx))
        {
            (debits, credits) = idx.DebitsAndCredits(period.From, period.To);
        }
        return new TrialBalanceRow(ledger, opening, debits, credits, opening + debits - credits);
    }

    internal static void RequireSingleYear(DateRange period)
    {
        if (FinancialYear.Containing(period.From) != FinancialYear.Containing(period.To))
            throw new ArgumentException("A report period must be inside one financial year.", nameof(period));
    }

    Money SumBefore(LedgerId ledger, DateOnly date) =>
        index.TryGetValue(ledger, out var idx) ? idx.SumBefore(date) : Money.Zero;

    static bool IsIncomeOrExpense(ChartOfAccounts chart, LedgerId ledger) =>
        chart.NatureOf(ledger) is AccountNature.Income or AccountNature.Expense;

    /// <summary>One ledger's postings sorted by date and voucher, with running totals.</summary>
    sealed class LedgerIndex
    {
        readonly PostingRecord[] postings;
        readonly Money[] runningTotal;

        public LedgerIndex(IEnumerable<PostingRecord> records)
        {
            postings = records.OrderBy(p => p.Date).ThenBy(p => p.VoucherId).ToArray();
            runningTotal = new Money[postings.Length + 1];
            for (var i = 0; i < postings.Length; i++) runningTotal[i + 1] = runningTotal[i] + postings[i].Amount;
        }

        /// <summary>Sum of postings dated strictly before the date.</summary>
        public Money SumBefore(DateOnly date) => runningTotal[FirstOnOrAfter(date)];

        public IEnumerable<PostingRecord> Between(DateOnly from, DateOnly to)
        {
            for (var i = FirstOnOrAfter(from); i < postings.Length && postings[i].Date <= to; i++) yield return postings[i];
        }

        public (Money Debits, Money Credits) DebitsAndCredits(DateOnly from, DateOnly to)
        {
            var debits = Money.Zero;
            var credits = Money.Zero;
            foreach (var p in Between(from, to))
            {
                if (p.Amount > Money.Zero) debits += p.Amount;
                else credits -= p.Amount;
            }
            return (debits, credits);
        }

        int FirstOnOrAfter(DateOnly date)
        {
            int lo = 0, hi = postings.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (postings[mid].Date < date) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
