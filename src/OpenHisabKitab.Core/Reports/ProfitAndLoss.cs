using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Reports;

/// <summary>A ledger and an amount shown on the side it belongs to (positive is normal for that side).</summary>
public sealed record ReportRow(LedgerId Ledger, Money Amount);

public sealed record ProfitAndLoss(
    DateRange Period,
    IReadOnlyList<ReportRow> Income,
    IReadOnlyList<ReportRow> Expenses,
    Money TotalIncome,
    Money TotalExpenses,
    Money NetProfit);
