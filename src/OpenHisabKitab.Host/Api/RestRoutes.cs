using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Data.CompanyDb;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Api;

/// <summary>The /api/v1 routes. Each one translates and calls one application service method; ApiGate has already authenticated.</summary>
public static class RestRoutes
{
    public static RouteGroupBuilder MapRestApi(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");
        v1.MapGet("/info", (HttpContext c) =>
        {
            _ = c.Caller();
            return TypedResults.Ok(new { version = HostVersion.Current });
        });
        var companies = v1.MapGroup("/companies");
        MapReads(companies);
        MapWrites(companies);
        return v1;
    }

    static void MapReads(RouteGroupBuilder co)
    {
        co.MapGet("", (HttpContext c, CompanyService companies) =>
            TypedResults.Ok(companies.List(c.Caller()).Select(ApiMap.ToDto).ToList()));

        co.MapGet("/{company:int}", (HttpContext c, CompanyService companies, int company) =>
            TypedResults.Ok(ApiMap.ToDto(company, companies.Get(c.Caller(), company))));

        co.MapGet("/{company:int}/years", (HttpContext c, CompanyService companies, int company) =>
            TypedResults.Ok(companies.Years(c.Caller(), company).Select(ApiMap.ToDto).ToList()));

        co.MapGet("/{company:int}/groups", (HttpContext c, MasterService masters, int company) =>
            TypedResults.Ok(masters.Chart(c.Caller(), company).Groups.Select(ApiMap.ToDto).ToList()));

        co.MapGet("/{company:int}/ledgers", (HttpContext c, MasterService masters, int company, string? search) =>
        {
            var ctx = c.Caller();
            var chart = masters.Chart(ctx, company);
            var openings = masters.Openings(ctx, company);
            return TypedResults.Ok(ApiParse.Search(chart.Ledgers, l => l.Name, search).Select(l => ApiMap.ToDto(l, openings)).ToList());
        });

        co.MapGet("/{company:int}/parties", (HttpContext c, MasterService masters, int company, string? search) =>
        {
            var ctx = c.Caller();
            var chart = masters.Chart(ctx, company);
            var parties = masters.Parties(ctx, company).Select(p => ApiMap.ToDto(p, chart)).ToList();
            return TypedResults.Ok(ApiParse.Search(parties, p => p.Name, search).ToList());
        });

        co.MapGet("/{company:int}/items", (HttpContext c, MasterService masters, int company) =>
            TypedResults.Ok(masters.Items(c.Caller(), company).Select(ApiMap.ToDto).ToList()));

        co.MapGet("/{company:int}/vouchers", (HttpContext c, VoucherService vouchers, MasterService masters, int company, string? from, string? to,
            string? type, string? search) =>
        {
            var ctx = c.Caller();
            IReadOnlyList<VoucherRow> list = string.IsNullOrWhiteSpace(search)
                ? vouchers.List(ctx, company, ApiParse.Range(from, to), ApiParse.Type(type))
                : Found(ctx, vouchers, company, search, from is null && to is null ? null : ApiParse.Range(from, to), ApiParse.Type(type));
            var chart = masters.Chart(ctx, company);
            var bills = vouchers.BillNumbers(ctx, company, list);
            return TypedResults.Ok(list.Select(v => ApiMap.ToDto(v, chart, bills)).ToList());
        });

        co.MapGet("/{company:int}/parties/{id:int}/open-bills", (HttpContext c, VoucherService vouchers, int company, int id, long? excludingVoucher) =>
            TypedResults.Ok(vouchers.OpenBills(c.Caller(), company, new LedgerId(id), excludingVoucher).Select(ApiMap.ToDto).ToList()));

        co.MapGet("/{company:int}/parties/{id:int}/opening-bills", (HttpContext c, MasterService masters, int company, int id) =>
            TypedResults.Ok(ApiMap.ToDto(masters.OpeningBills(c.Caller(), company, new LedgerId(id)))));

        co.MapGet("/{company:int}/vouchers/{id:long}", (HttpContext c, VoucherService vouchers, MasterService masters, int company, long id) =>
        {
            var ctx = c.Caller();
            var v = vouchers.Get(ctx, company, id);
            return TypedResults.Ok(ApiMap.ToDto(v, masters.Chart(ctx, company), vouchers.BillNumbers(ctx, company, [v])));
        });

        var reports = co.MapGroup("/{company:int}/reports");

        reports.MapGet("/trial-balance", (HttpContext c, ReportService r, MasterService masters, int company, string? from, string? to) =>
        {
            var ctx = c.Caller();
            var tb = r.TrialBalance(ctx, company, ApiParse.Range(from, to));
            return TypedResults.Ok(ApiMap.ToDto(tb, masters.Chart(ctx, company)));
        });

        reports.MapGet("/profit-and-loss", (HttpContext c, ReportService r, MasterService masters, int company, string? from, string? to) =>
        {
            var ctx = c.Caller();
            var pl = r.ProfitAndLoss(ctx, company, ApiParse.Range(from, to));
            return TypedResults.Ok(ApiMap.ToDto(pl, masters.Chart(ctx, company)));
        });

        reports.MapGet("/balance-sheet", (HttpContext c, ReportService r, MasterService masters, int company, string? asAt) =>
        {
            var ctx = c.Caller();
            var bs = r.BalanceSheet(ctx, company, ApiParse.Date(asAt, "asAt"));
            return TypedResults.Ok(ApiMap.ToDto(bs, masters.Chart(ctx, company)));
        });

        reports.MapGet("/ledger-statement", (HttpContext c, ReportService r, MasterService masters, VoucherService vouchers, int company, int? ledger, string? from, string? to) =>
        {
            var ctx = c.Caller();
            if (ledger is not { } id) throw new ServiceException(ServiceError.Invalid, "Add ledger.");
            var range = ApiParse.Range(from, to);
            var statement = r.Statement(ctx, company, new LedgerId(id), range);
            var numbers = vouchers.List(ctx, company, range, null).ToDictionary(v => v.Id, v => v.DisplayNumber);
            return TypedResults.Ok(ApiMap.ToDto(statement, masters.Chart(ctx, company), numbers));
        });
    }

    static void MapWrites(RouteGroupBuilder co)
    {
        co.MapPost("/{company:int}/groups", async (HttpContext c, MasterService masters, int company, GroupRequest r) =>
            Created(c, new IdDto((await masters.AddGroupAsync(c.Caller(), company, r.Name, new GroupId(r.ParentId))).Value)));

        co.MapPost("/{company:int}/ledgers", async (HttpContext c, MasterService masters, int company, LedgerRequest r) =>
            Created(c, new IdDto((await masters.AddLedgerAsync(c.Caller(), company, r.Name, new GroupId(r.GroupId))).Value)));

        co.MapPut("/{company:int}/ledgers/{id:int}/name", async (HttpContext c, MasterService masters, int company, int id, RenameRequest r) =>
        {
            await masters.RenameAsync(c.Caller(), company, new LedgerId(id), r.Name);
            return TypedResults.NoContent();
        });

        co.MapPost("/{company:int}/parties", async (HttpContext c, MasterService masters, int company, PartyRequest r) =>
            Created(c, new IdDto((await masters.SavePartyAsync(c.Caller(), company, r.Name, new GroupId(r.GroupId),
                ApiParse.Party(r, default, 0))).Value)));

        co.MapPut("/{company:int}/parties/{id:int}", async (HttpContext c, MasterService masters, int company, int id, PartyRequest r) =>
        {
            var party = ApiParse.Party(r, new LedgerId(id), ApiParse.RowVersion(r.RowVersion));
            await Conflicts(c, masters, company, () => masters.SavePartyAsync(c.Caller(), company, r.Name, new GroupId(r.GroupId), party));
            return TypedResults.Ok(new IdDto(id));
        });

        co.MapPut("/{company:int}/parties/{id:int}/opening-bills", async (HttpContext c, MasterService masters, int company, int id, OpeningBillsRequest r) =>
        {
            var bills = ApiParse.OpeningBills(r);
            var version = await Conflicts(c, masters, company, () => masters.SetOpeningBillsAsync(c.Caller(), company, new LedgerId(id), r.Version, bills));
            return TypedResults.Ok(new VersionDto(version));
        });

        co.MapPut("/{company:int}", async (HttpContext c, CompanyService companies, int company, CompanyUpdateRequest r) =>
        {
            var ctx = c.Caller();
            await companies.UpdateAsync(ctx, company, ApiParse.Profile(r, companies.Get(ctx, company).FirstFyStartYear));
            return TypedResults.NoContent();
        });

        co.MapPost("/{company:int}/items", async (HttpContext c, MasterService masters, int company, ItemRequest r) =>
            Created(c, new IdDto(await masters.SaveItemAsync(c.Caller(), company, ApiParse.Item(r, 0, 0)))));

        co.MapPut("/{company:int}/items/{id:int}", async (HttpContext c, MasterService masters, int company, int id, ItemRequest r) =>
        {
            var item = ApiParse.Item(r, id, ApiParse.RowVersion(r.RowVersion));
            await Conflicts(c, masters, company, () => masters.SaveItemAsync(c.Caller(), company, item));
            return TypedResults.Ok(new IdDto(id));
        });

        co.MapPost("/{company:int}/vouchers/journal", async (HttpContext c, VoucherService vouchers, int company, JournalRequest r) =>
            Saved(c, company, await vouchers.SaveJournalAsync(c.Caller(), company, ApiParse.Journal(r))));

        co.MapPost("/{company:int}/vouchers/invoice", async (HttpContext c, VoucherService vouchers, int company, InvoiceRequest r) =>
            Saved(c, company, await vouchers.SaveInvoiceAsync(c.Caller(), company, ApiParse.Invoice(r))));

        co.MapPut("/{company:int}/vouchers/{id:long}/journal",
            async (HttpContext c, VoucherService vouchers, MasterService masters, int company, long id, EditJournalRequest r) =>
            {
                var input = ApiParse.Journal(r.Voucher, "$.voucher");
                var s = await Conflicts(c, masters, company, () => vouchers.EditJournalAsync(c.Caller(), company, id, r.RowVersion, input));
                return TypedResults.Ok(new SavedDto(s.Id, s.DisplayNumber, s.RowVersion));
            });

        co.MapPut("/{company:int}/vouchers/{id:long}/invoice",
            async (HttpContext c, VoucherService vouchers, MasterService masters, int company, long id, EditInvoiceRequest r) =>
            {
                var input = ApiParse.Invoice(r.Voucher, "$.voucher");
                var s = await Conflicts(c, masters, company, () => vouchers.EditInvoiceAsync(c.Caller(), company, id, r.RowVersion, input));
                return TypedResults.Ok(new SavedDto(s.Id, s.DisplayNumber, s.RowVersion));
            });

        co.MapPost("/{company:int}/vouchers/{id:long}/cancel",
            async (HttpContext c, VoucherService vouchers, MasterService masters, int company, long id, CancelRequest r) =>
            {
                await Conflicts(c, masters, company, async () =>
                {
                    await vouchers.CancelAsync(c.Caller(), company, id, r.RowVersion, r.Reason);
                    return 0;
                });
                return TypedResults.NoContent();
            });
    }

    // Every match of a search, page by page; REST returns the whole list, as for an unsearched list.
    static List<VoucherRow> Found(CallContext ctx, VoucherService vouchers, int company, string search, DateRange? range, VoucherType? type)
    {
        var rows = new List<VoucherRow>();
        string? after = null;
        do
        {
            var page = vouchers.Find(ctx, company, new VoucherSearch(search, range, type), new PageRequest(1000, after));
            rows.AddRange(page.Rows);
            after = page.Next;
        } while (after is not null);
        return rows;
    }

    static Created<IdDto> Created(HttpContext c, IdDto id) => TypedResults.Created($"{c.Request.Path}/{id.Id}", id);

    // A retry that found its voucher answers 200 with alreadySaved, not 201, so an integrator can tell it from a new save.
    static Results<Created<SavedDto>, Ok<SavedDto>> Saved(HttpContext c, int company, SavedVoucher s)
    {
        var dto = new SavedDto(s.Id, s.DisplayNumber, s.RowVersion, s.AlreadySaved);
        return s.AlreadySaved ? TypedResults.Ok(dto) : TypedResults.Created($"/api/v1/companies/{company}/vouchers/{s.Id}", dto);
    }

    // A conflict carries the current record; it leaves in wire form, with ledger names from the company's chart.
    static async Task<T> Conflicts<T>(HttpContext c, MasterService masters, int company, Func<Task<T>> work)
    {
        try { return await work(); }
        catch (ServiceException e) when (e.Error == ServiceError.Conflict && e.Current is not null)
        {
            var chart = masters.Chart(c.Caller(), company);
            object? current = e.Current switch
            {
                VoucherRow v => ApiMap.ToDto(v, chart),
                Party p => ApiMap.ToDto(p, chart),
                Item i => ApiMap.ToDto(i),
                OpeningBills b => ApiMap.ToDto(b),
                _ => null,
            };
            throw new ServiceException(ServiceError.Conflict, e.Message, current);
        }
    }
}
