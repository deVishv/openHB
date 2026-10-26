using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Vouchers;

/// <summary>One ledger's share of a voucher. Positive is a debit, negative is a credit.</summary>
public sealed record PostingLine(LedgerId Ledger, Money Amount);

public static class Postings
{
    /// <summary>
    /// Merges lines for the same ledger, keeping the order of first appearance,
    /// and drops ledgers that net to zero, so a voucher has one line per ledger.
    /// </summary>
    public static IReadOnlyList<PostingLine> Consolidate(IEnumerable<PostingLine> lines)
    {
        var order = new List<LedgerId>();
        var totals = new Dictionary<LedgerId, Money>();
        foreach (var line in lines)
        {
            if (totals.TryGetValue(line.Ledger, out var sum))
            {
                totals[line.Ledger] = sum + line.Amount;
            }
            else
            {
                order.Add(line.Ledger);
                totals[line.Ledger] = line.Amount;
            }
        }
        return order.Where(l => totals[l] != Money.Zero).Select(l => new PostingLine(l, totals[l])).ToList();
    }
}
