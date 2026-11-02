using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>A statement line with what the screen shows about its voucher. Counter is the voucher's other ledgers, largest first.</summary>
public sealed record StatementLine(StatementRow Row, VoucherType Type, string Number, string Narration, IReadOnlyList<LedgerId> Counter);

/// <summary>One page of a ledger statement. Opening is the balance just before this page's first line; Next is null on the last page.</summary>
public sealed record StatementPage(LedgerId Ledger, DateRange Period, Money Opening, IReadOnlyList<StatementLine> Lines, Money Closing, string? Next);

/// <summary>Financial reports. Every figure comes from Core's one aggregation component (spec 5.1).</summary>
public sealed class ReportService(CompanyBooks books, CompanyHub hub)
{
    public TrialBalance TrialBalance(CallContext ctx, int company, DateRange period) =>
        Open(ctx, company, period).Books.TrialBalance(period);

    public ProfitAndLoss ProfitAndLoss(CallContext ctx, int company, DateRange period) =>
        Open(ctx, company, period).Books.ProfitAndLoss(period);

    public BalanceSheet BalanceSheet(CallContext ctx, int company, DateOnly asAt) =>
        Open(ctx, company, null).Books.BalanceSheet(asAt);

    public LedgerStatement Statement(CallContext ctx, int company, LedgerId ledger, DateRange period)
    {
        var (b, chart) = Open(ctx, company, period);
        if (!chart.Contains(ledger)) throw new ServiceException(ServiceError.NotFound, "No such ledger.");
        return b.Statement(ledger, period);
    }

    /// <summary>The balance sheet laid out, with the same date a year earlier once the books have a previous year.</summary>
    public StatementLayout BalanceSheetLayout(CallContext ctx, int company, DateOnly asAt, LayoutStyle style)
    {
        var (b, chart) = Open(ctx, company, null);
        var before = asAt.AddYears(-1);
        var previous = HasYear(b, before) ? b.BalanceSheet(before) : null;
        return ReportLayout.BalanceSheet(chart, b.BalanceSheet(asAt), previous, style);
    }

    /// <summary>The profit and loss account laid out, with the same period a year earlier once the books have a previous year.</summary>
    public StatementLayout ProfitAndLossLayout(CallContext ctx, int company, DateRange period, LayoutStyle style)
    {
        var (b, chart) = Open(ctx, company, period);
        var before = new DateRange(period.From.AddYears(-1), period.To.AddYears(-1));
        var previous = HasYear(b, before.From) ? b.ProfitAndLoss(before) : null;
        return ReportLayout.ProfitAndLoss(chart, b.ProfitAndLoss(period), previous, style);
    }

    public CashFlowStatement CashFlow(CallContext ctx, int company, DateRange period) =>
        Open(ctx, company, period).Books.CashFlow(period);

    /// <summary>
    /// One page of a ledger statement. The cursor is the date and voucher id of the last line sent, so a save between two
    /// requests never repeats or skips a line; each page's opening is the running balance just before its first line.
    /// </summary>
    public StatementPage StatementPage(CallContext ctx, int company, LedgerId ledger, DateRange period, PageRequest page)
    {
        var limit = Paging.Limit(page);
        var after = Paging.DateCursor(page.After);
        var statement = Statement(ctx, company, ledger, period);
        var rows = statement.Rows;
        var start = 0;
        if (after is { } a)
            while (start < rows.Count && (rows[start].Date < a.Date || (rows[start].Date == a.Date && rows[start].VoucherId <= a.Id))) start++;

        var taken = rows.Skip(start).Take(limit + 1).ToList();
        string? next = null;
        if (taken.Count > limit)
        {
            taken.RemoveAt(limit);
            next = Paging.DateCursor(taken[^1].Date, taken[^1].VoucherId);
        }
        var opening = start == 0 ? statement.Opening : rows[start - 1].Balance;

        IReadOnlyDictionary<long, VoucherHead> heads;
        using (var c = hub.Open(company)) heads = VoucherRepository.Heads(c, [.. taken.Select(r => r.VoucherId)]);
        var lines = taken.Select(r =>
        {
            var head = heads.GetValueOrDefault(r.VoucherId) ?? new VoucherHead(r.VoucherId, VoucherType.Journal, "", "");
            var counter = books.PostingsOf(company, r.VoucherId)
                .Where(p => p.Ledger != ledger)
                .OrderByDescending(p => p.Amount < Money.Zero ? -p.Amount : p.Amount)
                .Select(p => p.Ledger)
                .ToList();
            return new StatementLine(r, head.Type, head.Number, head.Narration, counter);
        }).ToList();
        return new StatementPage(ledger, period, opening, lines, statement.Closing, next);
    }

    static bool HasYear(Books b, DateOnly date) => FinancialYear.Containing(date).Start >= b.Start;

    (Books Books, ChartOfAccounts Chart) Open(CallContext ctx, int company, DateRange? period)
    {
        ctx.Require(Permission.ViewReports);
        ctx.RequireCompany(company);
        if (period is { } p && FinancialYear.Containing(p.From) != FinancialYear.Containing(p.To))
            throw new ServiceException(ServiceError.Invalid, "Choose a period within one financial year.");
        return books.GetWithChart(company);
    }
}
