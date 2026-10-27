using Grpc.Core;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Host.Tests.Grpc;
using static OpenHisabKitab.Host.Tests.Grpc.HostFactory;
using SessionClient = OpenHisabKitab.Contracts.V1.Session.SessionClient;

namespace OpenHisabKitab.Host.Tests;

public sealed class ConcurrencyTests : IDisposable
{
    const string Password = "owner-pass-1";
    static readonly Period Year = new() { From = "2026-04-01", To = "2027-03-31" };
    readonly HostFactory factory = new();
    readonly string owner;
    readonly int company, cash, sales;

    public ConcurrencyTests()
    {
        var channel = factory.Channel();
        owner = new SessionClient(channel).SetupOwner(new Credentials { Name = "owner", Password = Password }, Headers()).Token;
        company = new Companies.CompaniesClient(channel).Create(new CompanyProfileMsg
        {
            Name = "A Traders", State = "27", Registration = "Regular", Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
        }, Headers(owner)).Company;
        var chart = new Masters.MastersClient(channel).GetChart(new CompanyRef { Company = company }, Headers(owner));
        cash = chart.Ledgers.Single(l => l.Required == "Cash").Id;
        sales = chart.Ledgers.Single(l => l.Required == "Sales").Id;
    }

    public void Dispose() => factory.Dispose();

    string Login() => new SessionClient(factory.Channel()).Login(new Credentials { Name = "owner", Password = Password }, Headers()).Token;

    JournalMsg Receipt(long paise) => new()
    {
        Type = "Receipt", Date = "2026-05-10",
        Lines = { new PostingLineMsg { Ledger = cash, Paise = paise }, new PostingLineMsg { Ledger = sales, Paise = -paise } },
    };

    [Fact]
    public async Task Eight_savers_get_four_hundred_consecutive_numbers_while_readers_see_balanced_books()
    {
        var sessions = Enumerable.Range(0, 8).Select(_ => Login()).ToList();
        var numbers = new System.Collections.Concurrent.ConcurrentBag<string>();
        var total = 0L;
        using var stop = new CancellationTokenSource();
        var unbalanced = 0;

        var reader = Task.Run(async () =>
        {
            var reports = new Reports.ReportsClient(factory.Channel());
            while (!stop.IsCancellationRequested)
            {
                var tb = await reports.TrialBalanceAsync(new PeriodRequest { Company = company, Period = Year }, Headers(owner));
                if (tb.TotalDebit != tb.TotalCredit) Interlocked.Increment(ref unbalanced);
            }
        });

        await Task.WhenAll(sessions.Select((token, s) => Task.Run(async () =>
        {
            var client = new Vouchers.VouchersClient(factory.Channel());
            for (var i = 1; i <= 50; i++)
            {
                var paise = s * 1000 + i;
                var saved = await client.SaveJournalAsync(new JournalRequest { Company = company, Journal = Receipt(paise) }, Headers(token));
                numbers.Add(saved.DisplayNumber);
                Interlocked.Add(ref total, paise);
            }
        })));
        stop.Cancel();
        await reader;

        Assert.Equal(Enumerable.Range(1, 400).Select(n => $"RCT/26-27/{n:D5}"), numbers.Order(StringComparer.Ordinal));
        var final = new Reports.ReportsClient(factory.Channel()).TrialBalance(new PeriodRequest { Company = company, Period = Year }, Headers(owner));
        Assert.Equal(total, final.Rows.Single(r => r.Ledger == cash).Debits);
        Assert.Equal(0, unbalanced);

        var admin = new Admin.AdminClient(factory.Channel());
        var verified = admin.VerifyAudit(new AuditTarget { Company = company }, Headers(owner));
        Assert.True(verified.Ok);
        var voucherEntries = admin.ListAudit(new ListAuditRequest { Company = company, Entity = "voucher", Limit = 5000 }, Headers(owner));
        Assert.Equal(400, voucherEntries.Entries.Count);
    }

    [Fact]
    public async Task Two_editors_of_one_voucher_get_one_success_and_one_conflict()
    {
        var vouchers = new Vouchers.VouchersClient(factory.Channel());
        var saved = vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(100) }, Headers(owner));
        var (a, b) = (Login(), Login());

        async Task<StatusCode> Edit(string token, long paise)
        {
            try
            {
                await new Vouchers.VouchersClient(factory.Channel()).EditJournalAsync(new EditJournalRequest
                {
                    Company = company, Id = saved.Id, RowVersion = saved.RowVersion, Journal = Receipt(paise),
                }, Headers(token));
                return StatusCode.OK;
            }
            catch (RpcException e) { return e.StatusCode; }
        }

        var results = await Task.WhenAll(Task.Run(() => Edit(a, 200)), Task.Run(() => Edit(b, 300)));
        Assert.Equal([StatusCode.OK, StatusCode.Aborted], results.Order());
        Assert.Equal(2, vouchers.Get(new VoucherRef { Company = company, Id = saved.Id }, Headers(owner)).RowVersion);
    }
}
