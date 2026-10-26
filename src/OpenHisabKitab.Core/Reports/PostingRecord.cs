using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Reports;

/// <summary>One stored posting line with its voucher and date. Positive is a debit.</summary>
public sealed record PostingRecord(long VoucherId, DateOnly Date, LedgerId Ledger, Money Amount);
