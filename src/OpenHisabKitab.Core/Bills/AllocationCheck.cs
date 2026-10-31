using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Bills;

public enum AllocationError { NotPositive, UnknownBill, OtherParty, Duplicate, MoreThanOpen, MoreThanSettlement }

/// <summary>What is wrong with an allocation, and the bill it concerns. Bill is null for MoreThanSettlement.</summary>
public sealed record AllocationProblem(AllocationError Error, BillRef? Bill);

public static class AllocationCheck
{
    /// <summary>
    /// The first problem, or null. bills holds the party's open bills, without this voucher's own earlier allocations.
    /// settlement is what the voucher settles for the party, as a positive amount.
    /// </summary>
    public static AllocationProblem? Check(LedgerId party, Money settlement, IReadOnlyList<Allocation> allocations,
        IReadOnlyDictionary<BillRef, OpenBill> bills)
    {
        var seen = new HashSet<BillRef>();
        var total = Money.Zero;
        foreach (var a in allocations)
        {
            if (a.Amount <= Money.Zero) return new AllocationProblem(AllocationError.NotPositive, a.Bill);
            if (!bills.TryGetValue(a.Bill, out var bill)) return new AllocationProblem(AllocationError.UnknownBill, a.Bill);
            if (bill.Party != party) return new AllocationProblem(AllocationError.OtherParty, a.Bill);
            if (!seen.Add(a.Bill)) return new AllocationProblem(AllocationError.Duplicate, a.Bill);
            if (a.Amount > bill.Open) return new AllocationProblem(AllocationError.MoreThanOpen, a.Bill);
            total += a.Amount;
        }
        return total > settlement ? new AllocationProblem(AllocationError.MoreThanSettlement, null) : null;
    }
}
