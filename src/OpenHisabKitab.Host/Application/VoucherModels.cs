using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Host.Application;

/// <summary>A receipt, payment, journal, or contra as typed: ledger lines with signed amounts, positive for debit.</summary>
public sealed record JournalInput(VoucherType Type, DateOnly Date, string Narration, IReadOnlyList<PostingLine> Lines);

/// <summary>What the host assigned on save: the voucher's id, its number, and its row version for the next edit.</summary>
public sealed record SavedVoucher(long Id, string DisplayNumber, long RowVersion);
