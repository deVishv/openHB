using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Reports;

/// <summary>Credits is shown as a positive amount. Opening and Closing are signed: positive is debit.</summary>
public sealed record TrialBalanceRow(LedgerId Ledger, Money Opening, Money Debits, Money Credits, Money Closing);

/// <summary>
/// A non zero OpeningDifference is the amount by which opening balances fail to balance.
/// It is counted on the opposite side so the totals still agree; screens show it as "Opening difference".
/// </summary>
public sealed record TrialBalance(
    DateRange Period, IReadOnlyList<TrialBalanceRow> Rows, Money OpeningDifference, Money TotalDebit, Money TotalCredit);
