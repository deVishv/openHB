using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>Part of a settling voucher allocated to one bill of a party.</summary>
public sealed record AllocationRow(LedgerId Party, BillRef Bill, Money Amount);

/// <summary>A bill a party owed or was owed when the books started (spec 13, Opening balances). Id 0 is a new bill.</summary>
public sealed record OpeningBillRow(long Id, LedgerId Party, string Number, DateOnly Date, DateOnly? DueDate, Money Amount);

/// <summary>Port code and shipping bill of an export invoice. Each part is optional.</summary>
public sealed record ExportDetails(string? PortCode, string? ShippingBillNumber, DateOnly? ShippingBillDate);

public static class BillRepository
{
    static readonly string[] BillTypes = [nameof(Core.Vouchers.VoucherType.Sale), nameof(Core.Vouchers.VoucherType.Purchase),
        nameof(Core.Vouchers.VoucherType.CreditNote), nameof(Core.Vouchers.VoucherType.DebitNote)];

    /// <summary>The party's opening bills, by date, then id.</summary>
    public static IReadOnlyList<OpeningBillRow> OpeningBills(SqliteConnection c, LedgerId party) =>
        Sql.Query(c, "SELECT id, party_ledger_id, number, date, due_date, amount_paise FROM opening_bills WHERE party_ledger_id = $p ORDER BY date, id",
            r => new OpeningBillRow(r.GetInt64(0), new LedgerId(r.GetInt32(1)), r.GetString(2), Dates.Parse(r.GetString(3)),
                Dates.ParseNullable(r.NullableString(4)), new Money(r.GetInt64(5))), ("$p", party.Value));

    public static long OpeningBillsVersion(SqliteConnection c, LedgerId party) =>
        Sql.Query(c, "SELECT opening_bills_version FROM parties WHERE ledger_id = $p", r => r.GetInt64(0), ("$p", party.Value)).Single();

    /// <summary>
    /// Rows with Id 0 are inserted; rows with an Id are updated; stored rows not listed are deleted.
    /// The party's opening_bills_version goes up by one.
    /// </summary>
    public static void ReplaceOpeningBills(SqliteTransaction tx, LedgerId party, IReadOnlyList<OpeningBillRow> bills)
    {
        var keep = bills.Where(b => b.Id != 0).Select(b => b.Id).ToHashSet();
        foreach (var stored in OpeningBills(tx.Connection!, party).Where(b => !keep.Contains(b.Id)))
            Sql.Execute(tx, "DELETE FROM opening_bills WHERE id = $id", ("$id", stored.Id));
        // Updates first, under temporary numbers, so swapping two numbers never trips the unique index.
        foreach (var b in bills.Where(b => b.Id != 0))
            Sql.Execute(tx, "UPDATE opening_bills SET number = '~' || id WHERE id = $id AND party_ledger_id = $p", ("$id", b.Id), ("$p", party.Value));
        foreach (var b in bills)
        {
            var args = new (string, object?)[]
            {
                ("$id", b.Id), ("$p", party.Value), ("$n", b.Number), ("$d", Dates.Format(b.Date)), ("$due", Dates.Format(b.DueDate)),
                ("$a", b.Amount.Paise),
            };
            if (b.Id == 0)
                Sql.Execute(tx, "INSERT INTO opening_bills(party_ledger_id, number, date, due_date, amount_paise) VALUES ($p, $n, $d, $due, $a)", args);
            else
                Sql.Execute(tx, "UPDATE opening_bills SET number = $n, date = $d, due_date = $due, amount_paise = $a WHERE id = $id AND party_ledger_id = $p", args);
        }
        Sql.Execute(tx, "UPDATE parties SET opening_bills_version = opening_bills_version + 1 WHERE ledger_id = $p", ("$p", party.Value));
    }

    /// <summary>
    /// The party's bills that are not cancelled, with settled totals. When excludingVoucher is given, that voucher's own
    /// allocations are left out of Settled, and the voucher is not listed as a bill.
    /// </summary>
    public static IReadOnlyList<OpenBill> Bills(SqliteConnection c, LedgerId party, PartySide side, int creditDays, long? excludingVoucher)
    {
        var exclude = excludingVoucher ?? 0;
        var settled = Sql.Query(c, """
                SELECT bill_voucher_id, opening_bill_id, sum(amount_paise) FROM allocations
                WHERE party_ledger_id = $p AND voucher_id <> $x GROUP BY bill_voucher_id, opening_bill_id
                """,
                r => (Ref: r.IsDBNull(0) ? new BillRef(BillSource.Opening, r.GetInt64(1)) : new BillRef(BillSource.Voucher, r.GetInt64(0)),
                    Paise: r.GetInt64(2)),
                ("$p", party.Value), ("$x", exclude))
            .ToDictionary(x => x.Ref, x => new Money(x.Paise));
        Money Settled(BillRef b) => settled.GetValueOrDefault(b);

        var result = OpeningBills(c, party)
            .Select(b => new OpenBill(new BillRef(BillSource.Opening, b.Id), party, b.Number, null, b.Date, b.DueDate, b.Amount,
                Settled(new BillRef(BillSource.Opening, b.Id))))
            .ToList();

        var types = string.Join(", ", BillTypes.Select(t => $"'{t}'"));
        var vouchers = Sql.Query(c, $"""
                SELECT v.id, v.display_number, v.date, i.party_doc_number, sum(p.amount_paise)
                FROM vouchers v
                JOIN postings p ON p.voucher_id = v.id AND p.ledger_id = $p
                LEFT JOIN invoices i ON i.voucher_id = v.id
                WHERE v.cancelled = 0 AND v.type IN ({types}) AND v.id <> $x
                GROUP BY v.id
                """,
            r => (Id: r.GetInt64(0), Number: r.GetString(1), Date: Dates.Parse(r.GetString(2)), PartyDoc: r.NullableString(3),
                Net: new Money(r.GetInt64(4))),
            ("$p", party.Value), ("$x", exclude));
        foreach (var v in vouchers.Where(v => PartySides.IsBill(side, v.Net)))
        {
            var bill = new BillRef(BillSource.Voucher, v.Id);
            var amount = v.Net < Money.Zero ? -v.Net : v.Net;
            result.Add(new OpenBill(bill, party, v.Number, v.PartyDoc, v.Date, v.Date.AddDays(creditDays), amount, Settled(bill)));
        }
        return [.. result.OrderBy(b => b.Date).ThenBy(b => b.Ref.Source).ThenBy(b => b.Ref.Id)];
    }

    /// <summary>
    /// Every listed party's bills as they stood at the end of asAt (OUTS): opening bills, and bill vouchers dated on or
    /// before it that are not cancelled. Settled counts only allocations made by vouchers dated on or before asAt. Three
    /// queries in all, whatever the number of parties.
    /// </summary>
    public static IReadOnlyDictionary<LedgerId, IReadOnlyList<OpenBill>> AllBillsAsAt(SqliteConnection c, DateOnly asAt,
        IReadOnlyDictionary<LedgerId, (PartySide Side, int CreditDays)> parties)
    {
        var date = Dates.Format(asAt);
        var settled = Sql.Query(c, """
                SELECT a.bill_voucher_id, a.opening_bill_id, sum(a.amount_paise) FROM allocations a
                JOIN vouchers v ON v.id = a.voucher_id
                WHERE v.cancelled = 0 AND v.date <= $d GROUP BY a.bill_voucher_id, a.opening_bill_id
                """,
                r => (Ref: r.IsDBNull(0) ? new BillRef(BillSource.Opening, r.GetInt64(1)) : new BillRef(BillSource.Voucher, r.GetInt64(0)),
                    Paise: r.GetInt64(2)),
                ("$d", date))
            .ToDictionary(x => x.Ref, x => new Money(x.Paise));
        Money Settled(BillRef b) => settled.GetValueOrDefault(b);

        var result = parties.Keys.ToDictionary(p => p, _ => new List<OpenBill>());
        var openings = Sql.Query(c, "SELECT id, party_ledger_id, number, date, due_date, amount_paise FROM opening_bills WHERE date <= $d",
            r => new OpeningBillRow(r.GetInt64(0), new LedgerId(r.GetInt32(1)), r.GetString(2), Dates.Parse(r.GetString(3)),
                Dates.ParseNullable(r.NullableString(4)), new Money(r.GetInt64(5))), ("$d", date));
        foreach (var b in openings.Where(b => result.ContainsKey(b.Party)))
        {
            var bill = new BillRef(BillSource.Opening, b.Id);
            result[b.Party].Add(new OpenBill(bill, b.Party, b.Number, null, b.Date, b.DueDate, b.Amount, Settled(bill)));
        }

        var types = string.Join(", ", BillTypes.Select(t => $"'{t}'"));
        var vouchers = Sql.Query(c, $"""
                SELECT v.id, p.ledger_id, v.display_number, v.date, i.party_doc_number, sum(p.amount_paise), v.type
                FROM vouchers v
                JOIN postings p ON p.voucher_id = v.id
                LEFT JOIN invoices i ON i.voucher_id = v.id
                WHERE v.cancelled = 0 AND v.type IN ({types}) AND v.date <= $d
                GROUP BY v.id, p.ledger_id
                """,
            r => (Id: r.GetInt64(0), Party: new LedgerId(r.GetInt32(1)), Number: r.GetString(2), Date: Dates.Parse(r.GetString(3)),
                PartyDoc: r.NullableString(4), Net: new Money(r.GetInt64(5)), Type: Enum.Parse<Core.Vouchers.VoucherType>(r.GetString(6))),
            ("$d", date));
        foreach (var v in vouchers)
        {
            if (!parties.TryGetValue(v.Party, out var p) || !PartySides.IsBill(p.Side, v.Net)) continue;
            var bill = new BillRef(BillSource.Voucher, v.Id);
            var amount = v.Net < Money.Zero ? -v.Net : v.Net;
            result[v.Party].Add(new OpenBill(bill, v.Party, v.Number, v.PartyDoc, v.Date, v.Date.AddDays(p.CreditDays), amount, Settled(bill), v.Type));
        }
        return result.ToDictionary(x => x.Key,
            x => (IReadOnlyList<OpenBill>)[.. x.Value.OrderBy(b => b.Date).ThenBy(b => b.Ref.Source).ThenBy(b => b.Ref.Id)]);
    }

    public static Money SettledAgainst(SqliteConnection c, BillRef bill) =>
        new(Sql.Query(c, $"SELECT coalesce(sum(amount_paise), 0) FROM allocations WHERE {Column(bill.Source)} = $id",
            r => r.GetInt64(0), ("$id", bill.Id)).Single());

    /// <summary>The party and number of any bill, cancelled or not, or null when there is no such bill.</summary>
    public static (LedgerId Party, string Number)? Describe(SqliteConnection c, BillRef bill) =>
        Sql.Query(c, bill.Source == BillSource.Opening
                ? "SELECT party_ledger_id, number FROM opening_bills WHERE id = $id"
                : "SELECT i.party_ledger_id, v.display_number FROM vouchers v JOIN invoices i ON i.voucher_id = v.id WHERE v.id = $id",
            r => ((LedgerId Party, string Number)?)(new LedgerId(r.GetInt32(0)), r.GetString(1)), ("$id", bill.Id)).SingleOrDefault();

    public static long? VoucherByRequestId(SqliteConnection c, string requestId) =>
        Sql.Query(c, "SELECT id FROM vouchers WHERE request_id = $r", r => (long?)r.GetInt64(0), ("$r", requestId)).SingleOrDefault();

    internal static string Column(BillSource source) => source == BillSource.Voucher ? "bill_voucher_id" : "opening_bill_id";

    internal static IReadOnlyList<(long Voucher, AllocationRow Row)> AllocationsWhere(SqliteConnection c, string voucherIds, params (string, object?)[] args) =>
        Sql.Query(c, $"""
                SELECT voucher_id, party_ledger_id, bill_voucher_id, opening_bill_id, amount_paise FROM allocations
                WHERE voucher_id IN ({voucherIds}) ORDER BY rowid
                """,
            r => (r.GetInt64(0), new AllocationRow(new LedgerId(r.GetInt32(1)),
                r.IsDBNull(2) ? new BillRef(BillSource.Opening, r.GetInt64(3)) : new BillRef(BillSource.Voucher, r.GetInt64(2)),
                new Money(r.GetInt64(4)))), args);

    internal static void WriteAllocations(SqliteTransaction tx, long voucherId, IReadOnlyList<AllocationRow> allocations)
    {
        foreach (var a in allocations)
            Sql.Execute(tx, $"INSERT INTO allocations(voucher_id, party_ledger_id, {Column(a.Bill.Source)}, amount_paise) VALUES ($v, $p, $b, $a)",
                ("$v", voucherId), ("$p", a.Party.Value), ("$b", a.Bill.Id), ("$a", a.Amount.Paise));
    }

    internal static void DeleteAllocations(SqliteTransaction tx, long voucherId) =>
        Sql.Execute(tx, "DELETE FROM allocations WHERE voucher_id = $v", ("$v", voucherId));
}
