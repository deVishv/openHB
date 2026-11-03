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
    readonly Tokens.TokensClient tokens;

    public GrpcEndToEndTests()
    {
        var channel = factory.Channel();
        session = new(channel);
        companies = new(channel);
        masters = new(channel);
        vouchers = new(channel);
        reports = new(channel);
        admin = new(channel);
        tokens = new(channel);
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

    [Fact]
    public void Owner_creates_lists_and_revokes_a_token_over_grpc()
    {
        var owner = Owner();
        var created = tokens.Create(new CreateTokenRequest { Name = "scripts", ReadWrite = true }, Headers(owner));
        Assert.StartsWith("ohk_", created.Token, StringComparison.Ordinal);
        Assert.Equal(("scripts", true), (created.Name, created.ReadWrite));
        var listed = Assert.Single(tokens.List(new V1Empty(), Headers(owner)).Tokens);
        Assert.Equal((created.Id, "scripts", true, ""), (listed.Id, listed.Name, listed.ReadWrite, listed.RevokedAt));
        Assert.NotEmpty(listed.CreatedAt);
        tokens.Revoke(new TokenRef { Id = created.Id }, Headers(owner));
        Assert.NotEmpty(Assert.Single(tokens.List(new V1Empty(), Headers(owner)).Tokens).RevokedAt);
    }

    [Fact]
    public void Api_token_is_not_a_session_on_the_grpc_port()
    {
        var owner = Owner();
        var created = tokens.Create(new CreateTokenRequest { Name = "scripts" }, Headers(owner));
        var e = Assert.Throws<RpcException>(() => companies.List(new V1Empty(), Headers(created.Token)));
        Assert.Equal((StatusCode.Unauthenticated, "Log in again."), (e.StatusCode, e.Status.Detail));
    }

    [Fact]
    public void Owner_sets_a_host_setting_over_grpc()
    {
        var owner = Owner();
        Assert.True(admin.SetHostSetting(new HostSettingRequest { Key = "api.enabled", Json = "true" }, Headers(owner)).RestartNeeded);
        var list = admin.ListHostSettings(new V1Empty(), Headers(owner)).Settings;
        Assert.Equal(HostSettings.All.Count, list.Count);
        var enabled = list.Single(s => s.Key == "api.enabled");
        Assert.Equal(("true", "false", "bool", "true, false"), (enabled.Json, enabled.DefaultJson, enabled.Type, enabled.AllowedValues));
        Assert.Equal("Turns on the local REST API port.", enabled.Description);
        var e = Assert.Throws<RpcException>(() => admin.SetHostSetting(new HostSettingRequest { Key = "api.port", Json = "80" }, Headers(owner)));
        Assert.Equal((StatusCode.InvalidArgument, "api.port must be 1024 to 65535, not the gRPC port."), (e.StatusCode, e.Status.Detail));
    }
    // Plan 5: allocations, bills, find, paging, company update, and the lock read.

    (string Token, int Company, int Cash, int Sales, int Party) Books()
    {
        var token = Owner();
        var company = Company(token);
        var (cash, sales) = Ledgers(token, company);
        var group = masters.GetChart(new CompanyRef { Company = company }, Headers(token)).Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var party = (int)masters.SaveParty(new SavePartyRequest
        {
            Company = company, Name = "Ravi Stores", Group = group, Party = new PartyMsg { State = "27", CreditDays = 30 },
        }, Headers(token)).Id;
        return (token, company, cash, sales, party);
    }

    SavedReply Sale(string token, int company, int party, int sales, long paise) => vouchers.SaveInvoice(new InvoiceRequest
    {
        Company = company,
        Invoice = new InvoiceMsg
        {
            Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
            Lines = { new InvoiceLineMsg { Ledger = sales, Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = paise } },
        },
    }, Headers(token));

    [Fact]
    public void Receipt_with_allocation_round_trips_over_grpc()
    {
        var (token, company, cash, sales, party) = Books();
        var sale = Sale(token, company, party, sales, 1000_00);
        var receipt = new JournalMsg
        {
            Type = "Receipt", Date = "2026-06-10", RequestId = "r-1",
            Lines = { new PostingLineMsg { Ledger = cash, Paise = 400_00 }, new PostingLineMsg { Ledger = party, Paise = -400_00 } },
            Allocations = { new AllocationMsg { Party = party, BillSource = "Voucher", BillId = sale.Id, Paise = 400_00 } },
        };
        var saved = vouchers.SaveJournal(new JournalRequest { Company = company, Journal = receipt }, Headers(token));
        Assert.False(saved.AlreadySaved);
        Assert.True(vouchers.SaveJournal(new JournalRequest { Company = company, Journal = receipt }, Headers(token)).AlreadySaved);
        var read = vouchers.Get(new VoucherRef { Company = company, Id = saved.Id }, Headers(token));
        Assert.Equal(new AllocationMsg { Party = party, BillSource = "Voucher", BillId = sale.Id, Paise = 400_00 }, read.Allocations.Single());
    }

    [Fact]
    public void Open_bills_over_grpc()
    {
        var (token, company, _, sales, party) = Books();
        var sale = Sale(token, company, party, sales, 1000_00);
        var bill = vouchers.OpenBills(new OpenBillsRequest { Company = company, Party = party }, Headers(token)).Bills.Single();
        Assert.Equal(new BillMsg
        {
            Source = "Voucher", Id = sale.Id, Party = party, Number = "INV/26-27/00001", Date = "2026-06-01", DueDate = "2026-07-01",
            AmountPaise = 1000_00, SettledPaise = 0,
        }, bill);
        Assert.Empty(vouchers.OpenBills(new OpenBillsRequest { Company = company, Party = party, ExcludingVoucher = sale.Id }, Headers(token)).Bills);
    }

    [Fact]
    public void Export_details_round_trip_over_grpc()
    {
        var (token, company, _, sales, party) = Books();
        var saved = vouchers.SaveInvoice(new InvoiceRequest
        {
            Company = company,
            Invoice = new InvoiceMsg
            {
                Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "ExportUnderLut", PlaceOfSupply = "96",
                PortCode = "INNSA1", ShippingBillNumber = "1234567", ShippingBillDate = "2026-06-02",
                Lines = { new InvoiceLineMsg { Ledger = sales, Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = 100_00 } },
            },
        }, Headers(token));
        var inv = vouchers.Get(new VoucherRef { Company = company, Id = saved.Id }, Headers(token)).Invoice;
        Assert.Equal(("INNSA1", "1234567", "2026-06-02"), (inv.PortCode, inv.ShippingBillNumber, inv.ShippingBillDate));
    }

    [Fact]
    public void Opening_bills_conflict_carries_the_current_list()
    {
        var (token, company, _, _, party) = Books();
        var list = masters.GetOpeningBills(new LedgerRef { Company = company, Ledger = party }, Headers(token));
        var request = new SetOpeningBillsRequest
        {
            Company = company, Party = party, Version = list.Version,
            Bills = { new OpeningBillMsg { Number = "OB-1", Date = "2026-03-01", DueDate = "2026-03-31", AmountPaise = 500_00 } },
        };
        var version = masters.SetOpeningBills(request, Headers(token)).Version;
        Assert.Equal(list.Version + 1, version);
        var stored = masters.GetOpeningBills(new LedgerRef { Company = company, Ledger = party }, Headers(token));
        Assert.Equal(("OB-1", "2026-03-31", 500_00L), (stored.Bills.Single().Number, stored.Bills.Single().DueDate, stored.Bills.Single().AmountPaise));
        var e = Assert.Throws<RpcException>(() => masters.SetOpeningBills(request, Headers(token)));
        Assert.Equal(StatusCode.Aborted, e.StatusCode);
        var current = OpeningBillList.Parser.ParseFrom(e.Trailers.GetValueBytes(ErrorInterceptor.CurrentTrailer));
        Assert.Equal(version, current.Version);
    }

    [Fact]
    public void Find_and_get_by_number_over_grpc()
    {
        var (token, company, _, sales, party) = Books();
        var sale = Sale(token, company, party, sales, 1000_00);
        var found = vouchers.Find(new FindVouchersRequest { Company = company, Text = "ravi" }, Headers(token));
        Assert.Equal(sale.Id, found.Vouchers.Single().Id);
        Assert.Equal("", found.Next);
        var byNumber = vouchers.GetByNumber(new VoucherNumberRef { Company = company, Type = "Sale", Number = "INV/26-27/00001" }, Headers(token));
        Assert.Equal(sale.Id, byNumber.Id);
    }

    [Fact]
    public void Paged_parties_and_vouchers_over_grpc()
    {
        var (token, company, cash, sales, _) = Books();
        var group = masters.GetChart(new CompanyRef { Company = company }, Headers(token)).Groups.Single(g => g.Kind == "TradeReceivables").Id;
        masters.SaveParty(new SavePartyRequest { Company = company, Name = "Blr Mart", Group = group, Party = new PartyMsg { State = "29" } }, Headers(token));
        var first = masters.ListParties(new ListRequest { Company = company, Page = new PageMsg { Limit = 1 } }, Headers(token));
        Assert.NotEqual("", first.Next);
        var second = masters.ListParties(new ListRequest { Company = company, Page = new PageMsg { Limit = 1, After = first.Next } }, Headers(token));
        Assert.Equal("", second.Next);
        Assert.NotEqual(first.Parties.Single().Ledger, second.Parties.Single().Ledger);
        Assert.Empty(masters.ListItems(new ListRequest { Company = company }, Headers(token)).Items);

        vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 100) }, Headers(token));
        vouchers.SaveJournal(new JournalRequest { Company = company, Journal = Receipt(cash, sales, 200) }, Headers(token));
        var page = vouchers.List(new ListVouchersRequest
        {
            Company = company, Period = new Period { From = "2026-04-01", To = "2027-03-31" }, Page = new PageMsg { Limit = 1 },
        }, Headers(token));
        Assert.Single(page.Vouchers);
        Assert.NotEqual("", page.Next);
    }

    [Fact]
    public void Company_update_and_lock_read_over_grpc()
    {
        var (token, company, _, _, _) = Books();
        companies.Update(new UpdateCompanyRequest
        {
            Company = company,
            Profile = new CompanyProfileMsg
            {
                Name = "A Traders LLP", State = "27", Registration = "Regular", Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
            },
        }, Headers(token));
        Assert.Equal("A Traders LLP", companies.Get(new CompanyRef { Company = company }, Headers(token)).Name);
        Assert.Equal("", admin.GetLock(new CompanyRef { Company = company }, Headers(token)).LockedUpTo);
        admin.LockUpTo(new LockRequest { Company = company, Date = "2026-06-30" }, Headers(token));
        Assert.Equal("2026-06-30", admin.GetLock(new CompanyRef { Company = company }, Headers(token)).LockedUpTo);
    }
}
