using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Host.Api;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Mcp;

public sealed record McpCompany(int Id, string Name, IReadOnlyList<YearDto> Years);

public sealed record McpLedgerHit(int LedgerId, string Name, string Group, bool IsParty, string? Gstin, string? State);

/// <summary>A voucher in a list. Amount is the total of its debit lines.</summary>
public sealed record McpVoucherSummary(long Id, VoucherType Type, string Number, DateOnly Date, string Narration, bool Cancelled, Money Amount);

public sealed record McpStatement(
    int LedgerId, string Ledger, DateOnly From, DateOnly To, Money Opening, Money Closing, IReadOnlyList<StatementRowDto> Rows, int Omitted,
    string? Note);

/// <summary>MCP tools that read the books (spec 4.1). Each calls the same services as REST; ApiGate has authenticated the caller.</summary>
[McpServerToolType]
public sealed class ReadTools(
    IHttpContextAccessor http, CompanyService companies, MasterService masters, VoucherService vouchers, ReportService reports,
    ILogger<ReadTools> log)
{
    const string Amounts = " Amounts are strings like \"12345.60\", each shown with its Indian formatted text.";
    const string Dates = " Dates are yyyy-mm-dd.";

    CallContext Caller => (http.HttpContext ?? throw new ServiceException(ServiceError.NotAuthenticated, "Send a valid API token.")).Caller();

    [McpServerTool(Name = "list_companies", ReadOnly = true), Description("Lists the companies you can open, with their financial years.")]
    public Task<CallToolResult> ListCompanies() => McpResults.Run(log, "list_companies", () =>
    {
        var ctx = Caller;
        return companies.List(ctx).Select(c => new McpCompany(c.Id, c.Name, [.. companies.Years(ctx, c.Id).Select(ApiMap.ToDto)])).ToList();
    });

    [McpServerTool(Name = "search_ledgers", ReadOnly = true),
     Description("Finds ledgers and parties of a company by name, as the command prompt does. Parties carry their GSTIN and state.")]
    public Task<CallToolResult> SearchLedgers(int company, string query) => McpResults.Run(log, "search_ledgers", () =>
    {
        var ctx = Caller;
        var chart = masters.Chart(ctx, company);
        var parties = masters.Parties(ctx, company).ToDictionary(p => p.Ledger);
        var groups = chart.Groups.ToDictionary(g => g.Id, g => g.Name);
        var hits = ApiParse.Search(chart.Ledgers, l => l.Name, query)
            .Select(l => parties.TryGetValue(l.Id, out var p)
                ? new McpLedgerHit(l.Id.Value, l.Name, groups[l.Group], true, p.Gstin?.Value, p.State.Value)
                : new McpLedgerHit(l.Id.Value, l.Name, groups[l.Group], false, null, null))
            .ToList();
        return McpResults.Capped(hits);
    });

    [McpServerTool(Name = "ledger_statement", ReadOnly = true),
     Description("A ledger's statement for a period within one financial year: opening, each voucher, and closing." + Amounts + Dates)]
    public Task<CallToolResult> LedgerStatement(int company, int ledgerId, string from, string to) => McpResults.Run(log, "ledger_statement", () =>
    {
        var ctx = Caller;
        var range = ApiParse.Range(from, to);
        var statement = reports.Statement(ctx, company, new LedgerId(ledgerId), range);
        var numbers = vouchers.List(ctx, company, range, null).ToDictionary(v => v.Id, v => v.DisplayNumber);
        var dto = ApiMap.ToDto(statement, masters.Chart(ctx, company), numbers);
        var rows = McpResults.Capped(dto.Rows);
        return new McpStatement(dto.LedgerId, dto.Ledger, dto.From, dto.To, dto.Opening, dto.Closing, rows.Rows, rows.Omitted, rows.Note);
    });

    [McpServerTool(Name = "trial_balance", ReadOnly = true), Description("The trial balance for a period within one financial year." + Amounts + Dates)]
    public Task<CallToolResult> TrialBalance(int company, string from, string to) => McpResults.Run(log, "trial_balance", () =>
    {
        var ctx = Caller;
        return ApiMap.ToDto(reports.TrialBalance(ctx, company, ApiParse.Range(from, to)), masters.Chart(ctx, company));
    });

    [McpServerTool(Name = "profit_and_loss", ReadOnly = true), Description("The profit and loss account for a period within one financial year." + Amounts + Dates)]
    public Task<CallToolResult> ProfitAndLoss(int company, string from, string to) => McpResults.Run(log, "profit_and_loss", () =>
    {
        var ctx = Caller;
        return ApiMap.ToDto(reports.ProfitAndLoss(ctx, company, ApiParse.Range(from, to)), masters.Chart(ctx, company));
    });

    [McpServerTool(Name = "balance_sheet", ReadOnly = true), Description("The balance sheet as at a date." + Amounts + Dates)]
    public Task<CallToolResult> BalanceSheet(int company, string asAt) => McpResults.Run(log, "balance_sheet", () =>
    {
        var ctx = Caller;
        return ApiMap.ToDto(reports.BalanceSheet(ctx, company, ApiParse.Date(asAt, "asAt")), masters.Chart(ctx, company));
    });

    [McpServerTool(Name = "find_vouchers", ReadOnly = true),
     Description("Finds vouchers in a period, optionally of one type (sale, purchase, creditNote, debitNote, receipt, payment, journal, " +
                 "contra) and matching some text in the number, party name, amount, or narration. Returns at most 100." + Amounts + Dates)]
    public Task<CallToolResult> FindVouchers(int company, string from, string to, string? type = null, string? text = null) =>
        McpResults.Run(log, "find_vouchers", () =>
        {
            var ctx = Caller;
            var range = ApiParse.Range(from, to);
            var kind = ApiParse.Type(type);
            // With text, FIND's search runs, so a party name or an amount finds vouchers too (spec 6.3).
            IEnumerable<Data.CompanyDb.VoucherRow> rows = string.IsNullOrWhiteSpace(text)
                ? vouchers.List(ctx, company, range, kind)
                : vouchers.Find(ctx, company, new VoucherSearch(text, range), new PageRequest(McpResults.RowLimit + 1)).Rows
                    .Where(v => kind is null || v.Type == kind);
            var found = rows
                .Select(v => new McpVoucherSummary(v.Id, v.Type, v.DisplayNumber, v.Date, v.Narration, v.Cancelled,
                    Money.Sum(v.Postings.Where(p => p.Amount > Money.Zero).Select(p => p.Amount))))
                .ToList();
            return McpResults.Capped(found);
        });

    [McpServerTool(Name = "get_voucher", ReadOnly = true), Description("One voucher with its ledger lines and, for an invoice, its items and tax." + Amounts)]
    public Task<CallToolResult> GetVoucher(int company, long id) => McpResults.Run(log, "get_voucher", () =>
    {
        var ctx = Caller;
        var v = vouchers.Get(ctx, company, id);
        return ApiMap.ToDto(v, masters.Chart(ctx, company), vouchers.BillNumbers(ctx, company, [v]));
    });

    [McpServerTool(Name = "open_bills", ReadOnly = true),
     Description("A party's bills with what is still open, oldest first. Use the source and id to allocate a receipt, payment, or note to a bill." + Amounts + Dates)]
    public Task<CallToolResult> OpenBills(int company, int partyId) => McpResults.Run(log, "open_bills", () =>
        McpResults.Capped(vouchers.OpenBills(Caller, company, new LedgerId(partyId), null).Select(ApiMap.ToDto).ToList()));
}
