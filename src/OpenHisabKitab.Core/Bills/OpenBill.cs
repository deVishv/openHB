using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Bills;

/// <summary>
/// A bill as it stands. Amount is positive. Settled is what allocations already take from it. PartyDocNumber is the
/// party's own number for a document received from them, such as a supplier's invoice number. Type is the voucher's type
/// for a voucher bill when the reader filled it in, so a report can open the voucher.
/// </summary>
public sealed record OpenBill(
    BillRef Ref, LedgerId Party, string Number, string? PartyDocNumber, DateOnly Date, DateOnly? DueDate, Money Amount, Money Settled,
    Vouchers.VoucherType? Type = null)
{
    public Money Open => Amount - Settled;
}

/// <summary>Part of a settling voucher allocated to one bill.</summary>
public sealed record Allocation(BillRef Bill, Money Amount);
