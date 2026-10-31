using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Bills;

public static class BillAllocator
{
    /// <summary>
    /// Oldest first (spec 13): by date, then by Ref.Id. The bill named by first, if open, goes first. Each bill takes what
    /// is left, up to its open amount. Bills with nothing open are skipped. What is not allocated stays on account.
    /// </summary>
    public static IReadOnlyList<Allocation> Suggest(IReadOnlyList<OpenBill> bills, Money amount, BillRef? first = null)
    {
        var result = new List<Allocation>();
        var left = amount;
        var ordered = bills
            .Where(b => b.Open > Money.Zero)
            .OrderBy(b => first is { } f && b.Ref == f ? 0 : 1)
            .ThenBy(b => b.Date)
            .ThenBy(b => b.Ref.Id);
        foreach (var bill in ordered)
        {
            if (left <= Money.Zero) break;
            var take = bill.Open < left ? bill.Open : left;
            result.Add(new Allocation(bill.Ref, take));
            left -= take;
        }
        return result;
    }
}
