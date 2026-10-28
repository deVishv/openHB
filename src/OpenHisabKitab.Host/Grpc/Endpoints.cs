using System.Globalization;
using Grpc.Core;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Host.Application;
using V1Empty = OpenHisabKitab.Contracts.V1.Empty;

namespace OpenHisabKitab.Host.Grpc;

// Each endpoint only translates: message in, application service call, message out. Errors are mapped by ErrorInterceptor.

internal sealed class SessionEndpoint(AuthService auth, IRemoteAddressAccessor remote) : Contracts.V1.Session.SessionBase
{
    static readonly V1Empty None = new();

    public override Task<HostInfo> GetInfo(V1Empty request, ServerCallContext context) =>
        Task.FromResult(new HostInfo { Version = HostVersion.Current, NeedsSetup = auth.NeedsSetup() });

    public override Task<LoginReply> SetupOwner(Credentials request, ServerCallContext context)
    {
        var isLoopback = Loopback.Is(remote.RemoteAddress(context.GetHttpContext()));
        return Task.FromResult(Reply(auth.SetupOwner(request.Name, request.Password, context.Machine(), isLoopback)));
    }

    public override Task<LoginReply> Login(Credentials request, ServerCallContext context) =>
        Task.FromResult(Reply(auth.Login(request.Name, request.Password, context.Machine())));

    public override Task<V1Empty> Logout(V1Empty request, ServerCallContext context)
    {
        auth.Logout(context.Token());
        return Task.FromResult(None);
    }

    public override Task<V1Empty> ChangePassword(ChangePasswordRequest request, ServerCallContext context)
    {
        auth.ChangePassword(context.Caller(), context.Token(), request.Current, request.Next);
        return Task.FromResult(None);
    }

    static LoginReply Reply(Application.Session s)
    {
        var reply = new LoginReply
        {
            Token = s.Token, UserId = s.Context.UserId, UserName = s.Context.UserName,
            ExpiresAt = s.ExpiresAt.ToString("O", CultureInfo.InvariantCulture),
        };
        reply.Permissions.AddRange(s.Context.Permissions.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        return reply;
    }
}

internal sealed class CompaniesEndpoint(CompanyService companies) : Companies.CompaniesBase
{
    public override Task<CompanyList> List(V1Empty request, ServerCallContext context)
    {
        var list = new CompanyList();
        list.Companies.AddRange(companies.List(context.Caller()).Select(c => new CompanyEntry { Id = c.Id, Name = c.Name }));
        return Task.FromResult(list);
    }

    public override Task<CompanyRef> Create(CompanyProfileMsg request, ServerCallContext context) =>
        Task.FromResult(new CompanyRef { Company = companies.Create(context.Caller(), Wire.Profile(request)) });

    public override Task<CompanyProfileMsg> Get(CompanyRef request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(companies.Get(context.Caller(), request.Company)));

    public override Task<YearList> ListYears(CompanyRef request, ServerCallContext context)
    {
        var list = new YearList();
        list.Years.AddRange(companies.Years(context.Caller(), request.Company).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }

    public override async Task<YearMsg> AddYear(CompanyRef request, ServerCallContext context) =>
        Wire.ToMsg(await companies.AddYearAsync(context.Caller(), request.Company));

    public override async Task<V1Empty> SetSetting(SettingRequest request, ServerCallContext context)
    {
        await companies.SetSettingAsync(context.Caller(), request.Company, request.Key, request.Json);
        return new V1Empty();
    }
}

internal sealed class MastersEndpoint(MasterService masters) : Masters.MastersBase
{
    static async Task<V1Empty> Done(Task work)
    {
        await work;
        return new V1Empty();
    }

    public override Task<ChartMsg> GetChart(CompanyRef request, ServerCallContext context)
    {
        var ctx = context.Caller();
        return Task.FromResult(Wire.ToMsg(masters.Chart(ctx, request.Company), masters.Openings(ctx, request.Company)));
    }

    public override async Task<IdReply> AddGroup(AddGroupRequest request, ServerCallContext context) =>
        new() { Id = (await masters.AddGroupAsync(context.Caller(), request.Company, request.Name, new GroupId(request.Parent))).Value };

    public override async Task<IdReply> AddLedger(AddLedgerRequest request, ServerCallContext context) =>
        new() { Id = (await masters.AddLedgerAsync(context.Caller(), request.Company, request.Name, new GroupId(request.Group))).Value };

    public override Task<V1Empty> RenameGroup(RenameRequest request, ServerCallContext context) =>
        Done(masters.RenameGroupAsync(context.Caller(), request.Company, new GroupId(request.Id), request.Name));

    public override Task<V1Empty> RenameLedger(RenameRequest request, ServerCallContext context) =>
        Done(masters.RenameAsync(context.Caller(), request.Company, new LedgerId(request.Id), request.Name));

    public override Task<V1Empty> RemoveLedger(LedgerRef request, ServerCallContext context) =>
        Done(masters.RemoveLedgerAsync(context.Caller(), request.Company, new LedgerId(request.Ledger)));

    public override Task<V1Empty> SetOpening(SetOpeningRequest request, ServerCallContext context) =>
        Done(masters.SetOpeningAsync(context.Caller(), request.Company, new LedgerId(request.Ledger), new Money(request.Paise)));

    public override async Task<IdReply> SaveParty(SavePartyRequest request, ServerCallContext context)
    {
        var party = Wire.Party(request.Party ?? throw new ServiceException(ServiceError.Invalid, "Enter the party."));
        return new IdReply { Id = (await masters.SavePartyAsync(context.Caller(), request.Company, request.Name, new GroupId(request.Group), party)).Value };
    }

    public override Task<PartyList> ListParties(CompanyRef request, ServerCallContext context)
    {
        var list = new PartyList();
        list.Parties.AddRange(masters.Parties(context.Caller(), request.Company).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }

    public override async Task<IdReply> SaveItem(SaveItemRequest request, ServerCallContext context)
    {
        var item = Wire.Item(request.Item ?? throw new ServiceException(ServiceError.Invalid, "Enter the item."));
        return new IdReply { Id = await masters.SaveItemAsync(context.Caller(), request.Company, item) };
    }

    public override Task<ItemList> ListItems(CompanyRef request, ServerCallContext context)
    {
        var list = new ItemList();
        list.Items.AddRange(masters.Items(context.Caller(), request.Company).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }
}

internal sealed class VouchersEndpoint(VoucherService vouchers) : Vouchers.VouchersBase
{
    public override async Task<SavedReply> SaveJournal(JournalRequest request, ServerCallContext context) =>
        Wire.ToMsg(await vouchers.SaveJournalAsync(context.Caller(), request.Company, Wire.Journal(request.Journal)));

    public override async Task<SavedReply> EditJournal(EditJournalRequest request, ServerCallContext context) =>
        Wire.ToMsg(await vouchers.EditJournalAsync(context.Caller(), request.Company, request.Id, request.RowVersion, Wire.Journal(request.Journal)));

    public override async Task<SavedReply> SaveInvoice(InvoiceRequest request, ServerCallContext context) =>
        Wire.ToMsg(await vouchers.SaveInvoiceAsync(context.Caller(), request.Company, Wire.Invoice(request.Invoice)));

    public override async Task<SavedReply> EditInvoice(EditInvoiceRequest request, ServerCallContext context) =>
        Wire.ToMsg(await vouchers.EditInvoiceAsync(context.Caller(), request.Company, request.Id, request.RowVersion, Wire.Invoice(request.Invoice)));

    public override async Task<V1Empty> Cancel(CancelRequest request, ServerCallContext context)
    {
        await vouchers.CancelAsync(context.Caller(), request.Company, request.Id, request.RowVersion, request.Reason);
        return new V1Empty();
    }

    public override Task<VoucherMsg> Get(VoucherRef request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(vouchers.Get(context.Caller(), request.Company, request.Id)));

    public override Task<VoucherList> List(ListVouchersRequest request, ServerCallContext context)
    {
        VoucherType? type = request.Type.Length == 0 ? null : Wire.Enum<VoucherType>(request.Type, "voucher type");
        var list = new VoucherList();
        list.Vouchers.AddRange(vouchers.List(context.Caller(), request.Company, Wire.Range(request.Period), type).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }
}

internal sealed class ReportsEndpoint(ReportService reports) : Reports.ReportsBase
{
    public override Task<TrialBalanceMsg> TrialBalance(PeriodRequest request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(reports.TrialBalance(context.Caller(), request.Company, Wire.Range(request.Period))));

    public override Task<ProfitAndLossMsg> ProfitAndLoss(PeriodRequest request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(reports.ProfitAndLoss(context.Caller(), request.Company, Wire.Range(request.Period))));

    public override Task<BalanceSheetMsg> BalanceSheet(AsAtRequest request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(reports.BalanceSheet(context.Caller(), request.Company, Wire.Date(request.AsAt))));

    public override Task<StatementMsg> LedgerStatement(StatementRequest request, ServerCallContext context) =>
        Task.FromResult(Wire.ToMsg(reports.Statement(context.Caller(), request.Company, new LedgerId(request.Ledger), Wire.Range(request.Period))));
}

internal sealed class AdminEndpoint(AdminService admin, UserService users) : Admin.AdminBase
{
    static Task<V1Empty> Done() => Task.FromResult(new V1Empty());

    public override async Task<V1Empty> LockUpTo(LockRequest request, ServerCallContext context)
    {
        await admin.LockUpToAsync(context.Caller(), request.Company, Wire.Date(request.Date));
        return new V1Empty();
    }

    public override async Task<V1Empty> Unlock(CompanyRef request, ServerCallContext context)
    {
        await admin.UnlockAsync(context.Caller(), request.Company);
        return new V1Empty();
    }

    public override Task<AuditVerificationMsg> VerifyAudit(AuditTarget request, ServerCallContext context)
    {
        var v = admin.VerifyAudit(context.Caller(), Wire.Target(request.Company));
        return Task.FromResult(new AuditVerificationMsg { Count = v.Count, Ok = v.Ok, FirstBadSeq = v.FirstBadSeq ?? 0, Anchor = v.Anchor.ToString() });
    }

    public override Task<AuditList> ListAudit(ListAuditRequest request, ServerCallContext context)
    {
        var list = new AuditList();
        list.Entries.AddRange(admin.Audit(context.Caller(), Wire.Target(request.Company), Wire.Filter(request)).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }

    public override async Task<BackupMsg> BackupNow(AuditTarget request, ServerCallContext context) =>
        Wire.ToMsg(await admin.BackupNowAsync(context.Caller(), Wire.Target(request.Company)));

    public override Task<BackupList> ListBackups(AuditTarget request, ServerCallContext context)
    {
        var list = new BackupList();
        list.Backups.AddRange(admin.Backups(context.Caller(), Wire.Target(request.Company)).Select(Wire.ToMsg));
        return Task.FromResult(list);
    }

    public override async Task<V1Empty> Restore(RestoreRequest request, ServerCallContext context)
    {
        await admin.RestoreAsync(context.Caller(), request.Company, request.Path);
        return new V1Empty();
    }

    public override Task<IdReply> CreateUser(CreateUserRequest request, ServerCallContext context) =>
        Task.FromResult(new IdReply
        {
            Id = users.Create(context.Caller(), request.Name, request.Password, Wire.Enum<BuiltInRole>(request.Role, "role")),
        });

    public override Task<V1Empty> SetUserCompanies(SetUserCompaniesRequest request, ServerCallContext context)
    {
        users.SetCompanies(context.Caller(), request.UserId, [.. request.Companies]);
        return Done();
    }

    public override Task<UserList> ListUsers(V1Empty request, ServerCallContext context)
    {
        var list = new UserList();
        list.Users.AddRange(users.List(context.Caller()).Select(u => new UserMsg { Id = u.Id, Name = u.Name, Role = u.Role.ToString(), Active = u.Active }));
        return Task.FromResult(list);
    }
}

internal sealed class ChangesEndpoint(ChangeFeed feed, SessionStore sessions) : Changes.ChangesBase
{
    public override async Task Watch(V1Empty request, IServerStreamWriter<ChangeEventMsg> responseStream, ServerCallContext context)
    {
        var token = context.Token();
        await foreach (var e in feed.Subscribe(context.CancellationToken))
        {
            // The session is checked at every event, so logout, expiry, and lost company access end or filter the stream.
            var ctx = sessions.Peek(token) ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Log in again."));
            if (!ctx.AllCompanies && !ctx.Companies.Contains(e.CompanyId)) continue;
            await responseStream.WriteAsync(new ChangeEventMsg { Company = e.CompanyId, Kind = e.Kind.ToString(), EntityId = e.EntityId ?? 0 });
        }
    }
}
