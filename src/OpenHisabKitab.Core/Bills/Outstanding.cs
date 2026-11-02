using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Bills;

/// <summary>An open bill with its due date and age. DaysOverdue of zero or less is not due.</summary>
public sealed record OutstandingBill(OpenBill Bill, DateOnly Due, int DaysOverdue, int Bucket);

/// <summary>
/// One party's position on its side: Total is the ledger closing, positive when the party owes (receivable) or is owed
/// (payable). OnAccount is the part no bill explains, such as an unallocated receipt.
/// </summary>
public sealed record OutstandingParty(LedgerId Party, IReadOnlyList<OutstandingBill> Bills, IReadOnlyList<Money> Buckets,
    Money OnAccount, Money Total);

public sealed record OutstandingReport(DateOnly AsAt, PartySide Side, AgeingBuckets Ageing, IReadOnlyList<OutstandingParty> Parties,
    IReadOnlyList<Money> BucketTotals, Money OnAccountTotal, Money Total);

/// <summary>A party's ledger closing (debit positive) and its bills, both as at the report date.</summary>
public sealed record PartyPosition(LedgerId Party, Money Closing, IReadOnlyList<OpenBill> Bills);

/// <summary>
/// OUTS (spec 6.3) with the rules the owner approved on 2026-11-02 (owner review, Plan 6 item 1): bills age from their
/// due date, or their date when they have none; what no bill explains is on account and not aged. The closing comes
/// from the books (spec 5.1), so open bills plus on account always equal the ledger.
/// </summary>
public static class Outstanding
{
    public static OutstandingReport Build(DateOnly asAt, PartySide side, AgeingBuckets ageing, IEnumerable<PartyPosition> parties)
    {
        var rows = new List<OutstandingParty>();
        foreach (var p in parties)
        {
            var total = side == PartySide.Receivable ? p.Closing : -p.Closing;
            var bills = p.Bills.Where(b => b.Open > Money.Zero)
                .Select(b =>
                {
                    var due = b.DueDate ?? b.Date;
                    var days = asAt.DayNumber - due.DayNumber;
                    return new OutstandingBill(b, due, days, ageing.BucketOf(days));
                })
                .ToList();
            if (bills.Count == 0 && total == Money.Zero) continue;

            var buckets = new Money[ageing.Count];
            foreach (var b in bills) buckets[b.Bucket] += b.Bill.Open;
            var onAccount = total - Money.Sum(bills.Select(b => b.Bill.Open));
            rows.Add(new OutstandingParty(p.Party, bills, buckets, onAccount, total));
        }

        var totals = Enumerable.Range(0, ageing.Count).Select(i => Money.Sum(rows.Select(r => r.Buckets[i]))).ToList();
        return new OutstandingReport(asAt, side, ageing, rows, totals, Money.Sum(rows.Select(r => r.OnAccount)), Money.Sum(rows.Select(r => r.Total)));
    }
}
