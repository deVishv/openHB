namespace OpenHisabKitab.Host.Tests;

public sealed class ReportServiceTests : IDisposable
{
    static readonly DateRange Year = new FinancialYear(2026).Range;
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId[] ledgers;

    public ReportServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        ledgers =
        [
            chart.Required(RequiredLedger.Cash),
            host.Masters.AddLedgerAsync(owner, company, "HDFC Bank", chart.Group(GroupKind.BankAccounts)).GetAwaiter().GetResult(),
            chart.Required(RequiredLedger.Sales),
            chart.Required(RequiredLedger.Purchases),
            chart.Required(RequiredLedger.Capital),
        ];
    }

    public void Dispose() => host.Dispose();

    JournalInput RandomJournal(Random r)
    {
        var debit = ledgers[r.Next(ledgers.Length)];
        var credit = ledgers.Where(l => l != debit).ElementAt(r.Next(ledgers.Length - 1));
        var amount = new Money(r.Next(1, 10_000_00));
        return new JournalInput(VoucherType.Journal, Year.From.AddDays(r.Next(365)), "",
            [new PostingLine(debit, amount), new PostingLine(credit, -amount)]);
    }

    TrialBalance Fresh(DateRange period)
    {
        using var c = host.Hub.Open(company);
        var profile = CompanyRepository.GetProfile(c);
        return new Books(ChartRepository.Load(c), new FinancialYear(profile.FirstFyStartYear).Start, ChartRepository.Openings(c),
            VoucherRepository.AllPostings(c)).TrialBalance(period);
    }

    static void AssertSame(TrialBalance expected, TrialBalance actual)
    {
        Assert.Equal(expected.Rows, actual.Rows);
        Assert.Equal(expected.TotalDebit, actual.TotalDebit);
        Assert.Equal(expected.TotalCredit, actual.TotalCredit);
        Assert.Equal(expected.OpeningDifference, actual.OpeningDifference);
    }

    [Fact]
    public async Task Cache_matches_a_full_rebuild_after_saves_edits_and_cancels()
    {
        var r = new Random(20261027);
        var saved = new List<SavedVoucher>();
        _ = host.Reports.TrialBalance(owner, company, Year);
        for (var i = 0; i < 200; i++)
        {
            var roll = r.Next(10);
            if (roll < 7 || saved.Count == 0) saved.Add(await host.Vouchers.SaveJournalAsync(owner, company, RandomJournal(r)));
            else
            {
                var pick = r.Next(saved.Count);
                var v = saved[pick];
                if (host.Vouchers.Get(owner, company, v.Id).Cancelled) continue;
                if (roll < 9) saved[pick] = await host.Vouchers.EditJournalAsync(owner, company, v.Id, v.RowVersion, RandomJournal(r));
                else await host.Vouchers.CancelAsync(owner, company, v.Id, v.RowVersion, "test");
            }
            if (i % 50 == 0) AssertSame(Fresh(Year), host.Reports.TrialBalance(owner, company, Year));
        }
        AssertSame(Fresh(Year), host.Reports.TrialBalance(owner, company, Year));
        var q2 = new FinancialYear(2026).Quarter(2);
        AssertSame(Fresh(q2), host.Reports.TrialBalance(owner, company, q2));
        var tb = host.Reports.TrialBalance(owner, company, Year);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
    }

    [Fact]
    public async Task Parallel_edits_and_cancels_keep_the_cache_right()
    {
        var r = new Random(7);
        var vouchers = new List<long>();
        for (var i = 0; i < 5; i++) vouchers.Add((await host.Vouchers.SaveJournalAsync(owner, company, RandomJournal(r))).Id);
        _ = host.Reports.TrialBalance(owner, company, Year);

        async Task Loop(int seed)
        {
            var rnd = new Random(seed);
            for (var n = 0; n < 50; n++)
            {
                var id = vouchers[rnd.Next(vouchers.Count)];
                while (true)
                {
                    var current = host.Vouchers.Get(owner, company, id);
                    if (current.Cancelled) break;
                    try
                    {
                        if (seed == 3 && n == 45) await host.Vouchers.CancelAsync(owner, company, id, current.RowVersion, "test");
                        else await host.Vouchers.EditJournalAsync(owner, company, id, current.RowVersion, RandomJournal(rnd));
                        break;
                    }
                    catch (ServiceException e) when (e.Error is ServiceError.Conflict) { }
                    catch (ServiceException e) when (e.Error is ServiceError.Invalid && e.Message == "This voucher is cancelled.") { break; }
                }
                if (n % 10 == 0) _ = host.Reports.TrialBalance(owner, company, Year);
            }
        }

        await Task.WhenAll(Enumerable.Range(1, 4).Select(s => Task.Run(() => Loop(s))));
        AssertSame(Fresh(Year), host.Reports.TrialBalance(owner, company, Year));
    }

    [Fact]
    public async Task Opening_change_reaches_the_cache()
    {
        var cash = ledgers[0];
        var capital = ledgers[4];
        await host.Vouchers.SaveJournalAsync(owner, company, RandomJournal(new Random(1)));
        _ = host.Reports.TrialBalance(owner, company, Year);
        await host.Masters.SetOpeningAsync(owner, company, cash, Money.FromRupees(500));
        await host.Masters.SetOpeningAsync(owner, company, capital, Money.FromRupees(-500));
        var tb = host.Reports.TrialBalance(owner, company, Year);
        Assert.Equal(Money.FromRupees(500), tb.Rows.Single(x => x.Ledger == cash).Opening);
        AssertSame(Fresh(Year), tb);
    }

    [Fact]
    public async Task Other_reports_agree_with_core()
    {
        var r = new Random(3);
        for (var i = 0; i < 30; i++) await host.Vouchers.SaveJournalAsync(owner, company, RandomJournal(r));
        var pnl = host.Reports.ProfitAndLoss(owner, company, Year);
        var bs = host.Reports.BalanceSheet(owner, company, Year.To);
        Assert.Equal(pnl.NetProfit, bs.CurrentProfit);
        Assert.Equal(bs.TotalAssets, bs.TotalLiabilities);
        var statement = host.Reports.Statement(owner, company, ledgers[0], Year);
        Assert.Equal(host.Reports.TrialBalance(owner, company, Year).Rows.Single(x => x.Ledger == ledgers[0]).Closing, statement.Closing);
    }

    [Fact]
    public void Data_entry_cannot_run_reports()
    {
        var asha = host.NewUser("asha", BuiltInRole.DataEntry, company).Context;
        var ex = Assert.Throws<ServiceException>(() => host.Reports.TrialBalance(asha, company, Year));
        Assert.Equal(ServiceError.PermissionDenied, ex.Error);
    }

    [Fact]
    public void Period_crossing_march_is_refused()
    {
        var ex = Assert.Throws<ServiceException>(() =>
            host.Reports.TrialBalance(owner, company, new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31))));
        Assert.Equal(ServiceError.Invalid, ex.Error);
        Assert.Equal("Choose a period within one financial year.", ex.Message);
        Assert.Throws<ServiceException>(() =>
            host.Reports.Statement(owner, company, ledgers[0], new DateRange(new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31))));
    }

    [Fact]
    public void Unknown_ledger_statement_is_not_found()
    {
        var ex = Assert.Throws<ServiceException>(() => host.Reports.Statement(owner, company, new LedgerId(9999), Year));
        Assert.Equal(ServiceError.NotFound, ex.Error);
    }
}
