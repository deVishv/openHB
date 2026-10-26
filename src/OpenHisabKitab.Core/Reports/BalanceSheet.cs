using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Reports;

/// <summary>
/// Assets are debit positive; liabilities are credit positive. TotalLiabilities includes the
/// current year's profit to date and any opening difference, so the two totals always agree.
/// </summary>
public sealed record BalanceSheet(
    DateOnly AsAt,
    IReadOnlyList<ReportRow> Assets,
    IReadOnlyList<ReportRow> Liabilities,
    Money CurrentProfit,
    Money OpeningDifference,
    Money TotalAssets,
    Money TotalLiabilities);
