using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Bills;

/// <summary>Where a bill comes from: a voucher (an invoice or note) or a party's opening bill.</summary>
public enum BillSource { Voucher, Opening }

/// <summary>A bill's identity: a voucher id or an opening bill id.</summary>
public readonly record struct BillRef(BillSource Source, long Id);

/// <summary>Which side of a party's account its bills sit on: debit for receivables, credit for payables.</summary>
public enum PartySide { Receivable, Payable }

public static class PartySides
{
    /// <summary>True when a posting to the party raises what is owed (a bill); false when it settles bills.</summary>
    public static bool IsBill(PartySide side, Money posting) =>
        side == PartySide.Receivable ? posting > Money.Zero : posting < Money.Zero;
}
