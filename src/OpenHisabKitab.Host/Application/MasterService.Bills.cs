using System.Globalization;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>A party's opening bills and their version, which changes on every save of the bills.</summary>
public sealed record OpeningBills(long Version, IReadOnlyList<OpeningBillRow> Bills);

/// <summary>Party opening bills (spec 13, Opening balances): the bills behind a party's opening balance, so OUTS is right from day one.</summary>
public sealed partial class MasterService
{
    public OpeningBills OpeningBills(CallContext ctx, int company, LedgerId party)
    {
        ctx.RequireCompany(company);
        using var c = hub.Open(company);
        if (PartyRepository.Get(c, party) is null) throw new ServiceException(ServiceError.NotFound, "No such party.");
        return new OpeningBills(BillRepository.OpeningBillsVersion(c, party), BillRepository.OpeningBills(c, party));
    }

    /// <summary>Replaces the party's opening bills. Returns their new version. The party's own row version does not change.</summary>
    public Task<long> SetOpeningBillsAsync(CallContext ctx, int company, LedgerId party, long version, IReadOnlyList<OpeningBillRow> bills)
    {
        ctx.Require(Permission.EditOpeningsAndRequiredLedgers);
        ctx.Require(Permission.EditMasters);
        ctx.RequireCompany(company);
        return hub.WriteAsync(company, tx =>
        {
            var c = tx.Connection!;
            if (PartyRepository.Get(c, party) is null) throw new ServiceException(ServiceError.NotFound, "No such party.");
            var current = new OpeningBills(BillRepository.OpeningBillsVersion(c, party), BillRepository.OpeningBills(c, party));
            if (current.Version != version)
                throw new ServiceException(ServiceError.Conflict, "Someone else changed these opening bills. Showing the current version.", current);
            var booksStart = new FinancialYear(CompanyRepository.GetProfile(c).FirstFyStartYear).Start;
            if (LockRepository.LockedUpTo(c) is { } locked && locked >= booksStart)
                throw new ServiceException(ServiceError.Locked, $"Books are locked up to {Text.Date(locked)}.");

            var rows = Checked(bills, party, booksStart, current.Bills);
            // A bill that vouchers settle cannot go, or drop below what they settle.
            foreach (var stored in current.Bills)
            {
                var settled = BillRepository.SettledAgainst(c, new BillRef(BillSource.Opening, stored.Id));
                if (settled == Money.Zero) continue;
                var kept = rows.FirstOrDefault(r => r.Id == stored.Id);
                if (kept is null || kept.Amount < settled)
                    throw new ServiceException(ServiceError.Invalid,
                        $"Bill {stored.Number} has {IndianNumberFormat.Format(settled)} settled. Change those vouchers first.");
            }

            BillRepository.ReplaceOpeningBills(tx, party, rows);
            var after = BillRepository.OpeningBills(c, party);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "set-opening-bills", "party", Text.Key(party.Value), Json(current.Bills), Json(after)));
            return BillRepository.OpeningBillsVersion(c, party);
        }, _ => feed.Publish(new ChangeEvent(company, ChangeKind.Masters, party.Value)));
    }

    static List<OpeningBillRow> Checked(IReadOnlyList<OpeningBillRow> bills, LedgerId party, DateOnly booksStart, IReadOnlyList<OpeningBillRow> stored)
    {
        var known = stored.Select(b => b.Id).ToHashSet();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<OpeningBillRow>();
        for (var i = 0; i < bills.Count; i++)
        {
            var b = bills[i] with { Party = party, Number = bills[i].Number.Trim() };
            var line = (i + 1).ToString(CultureInfo.InvariantCulture);
            ServiceException Bad(string text) => new(ServiceError.Invalid, $"Line {line}: {text}");
            if (b.Number.Length == 0) throw Bad("enter the bill number.");
            if (!numbers.Add(b.Number)) throw Bad($"bill {b.Number} is listed twice.");
            if (b.Date >= booksStart) throw Bad($"an opening bill must be dated before {Text.Date(booksStart)}.");
            if (b.DueDate is { } due && due < b.Date) throw Bad("the due date is before the bill date.");
            if (b.Amount <= Money.Zero) throw Bad("enter an amount more than zero.");
            if (b.Id != 0 && !known.Contains(b.Id)) throw Bad("unknown bill.");
            rows.Add(b);
        }
        return rows;
    }

    /// <summary>The side a party's bills sit on: receivable under Trade Receivables, otherwise payable.</summary>
    internal static PartySide SideOf(ChartOfAccounts chart, LedgerId party) =>
        IsUnder(chart, chart.Ledger(party).Group, GroupKind.TradeReceivables) ? PartySide.Receivable : PartySide.Payable;
}
