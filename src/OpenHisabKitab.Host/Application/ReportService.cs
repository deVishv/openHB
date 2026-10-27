using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Host.Application;

/// <summary>Financial reports. Every figure comes from Core's one aggregation component (spec 5.1).</summary>
public sealed class ReportService(CompanyBooks books)
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

    (Books Books, ChartOfAccounts Chart) Open(CallContext ctx, int company, DateRange? period)
    {
        ctx.Require(Permission.ViewReports);
        ctx.RequireCompany(company);
        if (period is { } p && FinancialYear.Containing(p.From) != FinancialYear.Containing(p.To))
            throw new ServiceException(ServiceError.Invalid, "Choose a period within one financial year.");
        return books.GetWithChart(company);
    }
}
