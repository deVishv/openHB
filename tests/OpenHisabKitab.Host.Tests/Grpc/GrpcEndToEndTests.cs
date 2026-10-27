using Grpc.Core;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Host.Grpc;
using static OpenHisabKitab.Host.Tests.Grpc.HostFactory;
using V1Empty = OpenHisabKitab.Contracts.V1.Empty;
using SessionClient = OpenHisabKitab.Contracts.V1.Session.SessionClient;

namespace OpenHisabKitab.Host.Tests.Grpc;

public sealed class GrpcEndToEndTests : IDisposable
{
    const string Password = "owner-pass-1";
    readonly HostFactory factory = new();
    readonly SessionClient session;
    readonly Companies.CompaniesClient companies;
    readonly Masters.MastersClient masters;
    readonly Vouchers.VouchersClient vouchers;
    readonly Reports.ReportsClient reports;
    readonly Admin.AdminClient admin;

    public GrpcEndToEndTests()
    {
        var channel = factory.Channel();
        session = new(channel);
        companies = new(channel);
        masters = new(channel);
        vouchers = new(channel);
        reports = new(channel);
        admin = new(channel);
    }

    public void Dispose() => factory.Dispose();

    string Owner() => session.SetupOwner(new Credentials { Name = "owner", Password = Password }, Headers()).Token;

    int Company(string token) => companies.Create(new CompanyProfileMsg
    {
        Name = "A Traders", State = "27", Registration = "Regular", Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
    }, Headers(token)).Company;

    (int Cash, int Sales) Ledgers(string token, int company)
    {
        var chart = masters.GetChart(new CompanyRef { Company = company }, Headers(token));
        return (chart.Ledgers.Single(l => l.Required == "Cash").Id, chart.Ledgers.Single(l => l.Required == "Sales").Id);
    }

    static JournalMsg Receipt(int cash, int sales, long paise) => new()
    {
        Type = "Receipt", Date = "2026-05-10", Narration = "cash sale",
        Lines = { new PostingLineMsg { Ledger = cash, Paise = paise }, new PostingLineMsg { Ledger = sales, Paise = -paise } },
    };

    [Fact]
    public void Get_info_reports_setup_needed_without_any_headers()
    {
        var info = session.GetInfo(new V1Empty());
        Assert.True(info.NeedsSetup);
        Assert.Equal(HostVersion.Current, info.Version);
    }

    [Fact]
    public void Setup_login_create_save_and_report()
    {
        Owner();
        var token = session.Login(new Credentials { Name = "owner", Password = Password }, Headers()).Token;
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);

        var saved = vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 50_000) }, Headers(token));
        Assert.Equal("RCT/26-27/00001", saved.DisplayNumber);

        var tb = reports.TrialBalance(new PeriodRequest { Company = company, Period = new Period { From = "2026-04-01", To = "2027-03-31" } },
            Headers(token));
        Assert.Equal(50_000, tb.Rows.Single(r => r.Ledger == cash).Debits);
        Assert.Equal(tb.TotalDebit, tb.TotalCredit);
    }

    [Fact]
    public void Wrong_version_is_refused_with_the_version_to_install()
    {
        var ex = Assert.Throws<RpcException>(() =>
            session.Login(new Credentials { Name = "owner", Password = Password }, Headers(version: "0.0.1")));
        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        Assert.Equal($"Install Open Hisab Kitab {HostVersion.Current} to connect.", ex.Status.Detail);
    }

    [Fact]
    public void Missing_token_is_unauthenticated()
    {
        Owner();
        var ex = Assert.Throws<RpcException>(() => companies.List(new V1Empty(), Headers()));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
        var bad = Assert.Throws<RpcException>(() => companies.List(new V1Empty(), Headers("not-a-token")));
        Assert.Equal(StatusCode.Unauthenticated, bad.StatusCode);
    }

    [Fact]
    public void Service_errors_keep_their_message()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var unbalanced = Receipt(cash, sales, 100);
        unbalanced.Lines[1].Paise = -99;
        var ex = Assert.Throws<RpcException>(() =>
            vouchers.SaveJournal(new JournalRequest { Company = company, Journal = unbalanced }, Headers(token)));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Equal("Debits and credits differ.", ex.Status.Detail);
    }

    [Fact]
    public void Bad_date_text_is_invalid_argument()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var j = Receipt(cash, sales, 100);
        j.Date = "10-05-2026";
        var ex = Assert.Throws<RpcException>(() => vouchers.SaveJournal(new JournalRequest { Company = company, Journal = j }, Headers(token)));
        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Equal("Use dates as yyyy-mm-dd.", ex.Status.Detail);
    }

    [Fact]
    public void Stale_edit_is_aborted_with_the_current_voucher()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var saved = vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 100) }, Headers(token));
        vouchers.EditJournal(new EditJournalRequest { Company = company, Id = saved.Id, RowVersion = 1, Journal = Receipt(cash, sales, 200) },
            Headers(token));
        var ex = Assert.Throws<RpcException>(() => vouchers.EditJournal(
            new EditJournalRequest { Company = company, Id = saved.Id, RowVersion = 1, Journal = Receipt(cash, sales, 300) }, Headers(token)));
        Assert.Equal(StatusCode.Aborted, ex.StatusCode);
        var current = VoucherMsg.Parser.ParseFrom(ex.Trailers.GetValueBytes("ohk-current-bin"));
        Assert.Equal(2, current.RowVersion);
        Assert.Equal(200, current.Postings.Single(p => p.Ledger == cash).Paise);
    }

    [Fact]
    public async Task Watch_sees_a_save_by_another_session()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var other = session.Login(new Credentials { Name = "owner", Password = Password }, Headers()).Token;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var watch = new Changes.ChangesClient(factory.Channel()).Watch(new V1Empty(), Headers(token), cancellationToken: cts.Token);
        await Task.Delay(300);
        var saved = vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 100) }, Headers(other));
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token));
        var e = watch.ResponseStream.Current;
        Assert.Equal(new ChangeEventMsg { Company = company, Kind = "Voucher", EntityId = saved.Id }, e);
    }

    [Fact]
    public void Data_entry_cannot_run_reports()
    {
        var token = Owner();
        var company = Company(token);
        var userId = admin.CreateUser(new CreateUserRequest { Name = "asha", Password = "asha-pass-1", Role = "DataEntry" }, Headers(token)).Id;
        admin.SetUserCompanies(new SetUserCompaniesRequest { UserId = (int)userId, Companies = { company } }, Headers(token));
        var asha = session.Login(new Credentials { Name = "asha", Password = "asha-pass-1" }, Headers()).Token;
        var ex = Assert.Throws<RpcException>(() => reports.TrialBalance(
            new PeriodRequest { Company = company, Period = new Period { From = "2026-04-01", To = "2027-03-31" } }, Headers(asha)));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public void Logout_ends_the_session()
    {
        var token = Owner();
        session.Logout(new V1Empty(), Headers(token));
        Assert.Equal(StatusCode.Unauthenticated,
            Assert.Throws<RpcException>(() => companies.List(new V1Empty(), Headers(token))).StatusCode);
    }

    [Fact]
    public void Setup_from_another_pc_is_refused()
    {
        using var remote = new HostFactory(callerIsLoopback: false);
        var client = new SessionClient(remote.Channel());
        var ex = Assert.Throws<RpcException>(() => client.SetupOwner(new Credentials { Name = "owner", Password = Password }, Headers()));
        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Equal("Set up the Owner on the host PC.", ex.Status.Detail);
    }

    [Fact]
    public async Task Watch_ends_after_logout()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var other = session.Login(new Credentials { Name = "owner", Password = Password }, Headers()).Token;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var watch = new Changes.ChangesClient(factory.Channel()).Watch(new V1Empty(), Headers(token), cancellationToken: cts.Token);
        await Task.Delay(300);
        session.Logout(new V1Empty(), Headers(token));
        vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 100) }, Headers(other));
        var ex = await Assert.ThrowsAsync<RpcException>(() => watch.ResponseStream.MoveNext(cts.Token));
        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }
}
