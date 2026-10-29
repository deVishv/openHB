using OpenHisabKitab.Core.Accounts;
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
        MapReads(v1.MapGroup("/companies"));
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

        co.MapGet("/{company:int}/vouchers", (HttpContext c, VoucherService vouchers, MasterService masters, int company, string? from, string? to, string? type) =>
        {
            var ctx = c.Caller();
            var range = ApiParse.Range(from, to);
            var list = vouchers.List(ctx, company, range, ApiParse.Type(type));
            var chart = masters.Chart(ctx, company);
            return TypedResults.Ok(list.Select(v => ApiMap.ToDto(v, chart)).ToList());
        });

        co.MapGet("/{company:int}/vouchers/{id:long}", (HttpContext c, VoucherService vouchers, MasterService masters, int company, long id) =>
        {
            var ctx = c.Caller();
            var v = vouchers.Get(ctx, company, id);
            return TypedResults.Ok(ApiMap.ToDto(v, masters.Chart(ctx, company)));
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
}
