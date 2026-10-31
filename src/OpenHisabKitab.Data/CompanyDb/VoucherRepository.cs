using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Data.CompanyDb;

public sealed record InvoiceLineRow(
    int? ItemId, LedgerId Ledger, string Description, string HsnSac, Quantity Quantity, Money UnitPrice, Money Discount,
    GstRate Rate, GstRate CessRate, bool PriceIncludesTax,
    Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess);

public sealed record InvoiceHeader(
    LedgerId Party, TradeDirection Direction, InvoiceKind Kind, StateCode PlaceOfSupply, bool ReverseCharge,
    string? PartyDocNumber, DateOnly? PartyDocDate, string? OriginalNumber, DateOnly? OriginalDate,
    Money RoundOff, IReadOnlyList<InvoiceLineRow> Lines, ExportDetails? Export = null);

/// <summary>
/// A stored voucher. Postings and allocations are empty once cancelled; the invoice detail is kept for the GSTR-1 document
/// summary. Allocations are the bills this voucher settles. RequestId is the client's id for the save that created it.
/// </summary>
public sealed record VoucherRow(
    long Id, VoucherType Type, string Series, int FyStartYear, long Number, string DisplayNumber, DateOnly Date,
    string Narration, bool Cancelled, int CreatedBy, long RowVersion,
    IReadOnlyList<PostingLine> Postings, InvoiceHeader? Invoice, IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null)
{
    public IReadOnlyList<AllocationRow> Allocations { get; init; } = Allocations ?? [];
}

public static class VoucherRepository
{
    const string Columns = "id, type, series, fy_start, number, display_number, date, narration, cancelled, created_by, row_version, request_id";

    /// <summary>Inserts with row version 1. The input's Id and RowVersion are ignored.</summary>
    public static long Insert(SqliteTransaction tx, VoucherRow v)
    {
        var id = Sql.InsertReturningId(tx, """
            INSERT INTO vouchers(type, series, fy_start, number, display_number, date, narration, cancelled, created_by, request_id)
            VALUES ($type, $series, $fy, $number, $display, $date, $narration, $cancelled, $by, $request)
            """,
            ("$type", v.Type.ToString()), ("$series", v.Series), ("$fy", v.FyStartYear), ("$number", v.Number),
            ("$display", v.DisplayNumber), ("$date", Dates.Format(v.Date)), ("$narration", v.Narration),
            ("$cancelled", v.Cancelled ? 1 : 0), ("$by", v.CreatedBy), ("$request", v.RequestId));
        WriteDetails(tx, id, v);
        return id;
    }

    /// <summary>Replaces date, narration, postings, and invoice detail. Type, series, and number never change. False when the row version is stale.</summary>
    public static bool Update(SqliteTransaction tx, VoucherRow v, long expectedRowVersion)
    {
        var changed = Sql.Execute(tx, """
            UPDATE vouchers SET date = $date, narration = $narration, row_version = row_version + 1
            WHERE id = $id AND row_version = $rv
            """, ("$date", Dates.Format(v.Date)), ("$narration", v.Narration), ("$id", v.Id), ("$rv", expectedRowVersion));
        if (changed == 0) return false;
        DeleteDetails(tx, v.Id);
        WriteDetails(tx, v.Id, v);
        return true;
    }

    /// <summary>Marks the voucher cancelled and removes its postings and allocations. False when the row version is stale.</summary>
    public static bool Cancel(SqliteTransaction tx, long id, long expectedRowVersion)
    {
        var changed = Sql.Execute(tx, """
            UPDATE vouchers SET cancelled = 1, row_version = row_version + 1 WHERE id = $id AND row_version = $rv
            """, ("$id", id), ("$rv", expectedRowVersion));
        if (changed == 0) return false;
        Sql.Execute(tx, "DELETE FROM postings WHERE voucher_id = $id", ("$id", id));
        BillRepository.DeleteAllocations(tx, id);
        return true;
    }

    public static VoucherRow? Get(SqliteConnection c, long id) =>
        Load(c, "id = $id", ("$id", id)).SingleOrDefault();

    /// <summary>Vouchers in the range, by date then id. A createdBy filter limits the list to one user's vouchers.</summary>
    public static IReadOnlyList<VoucherRow> List(SqliteConnection c, DateRange range, VoucherType? type, int? createdBy)
    {
        var where = "date >= $from AND date <= $to";
        var args = new List<(string, object?)> { ("$from", Dates.Format(range.From)), ("$to", Dates.Format(range.To)) };
        if (type is { } t) { where += " AND type = $type"; args.Add(("$type", t.ToString())); }
        if (createdBy is { } by) { where += " AND created_by = $by"; args.Add(("$by", by)); }
        return Load(c, where, [.. args]);
    }

    public static IReadOnlyList<PostingRecord> AllPostings(SqliteConnection c) =>
        Sql.Query(c, "SELECT voucher_id, date, ledger_id, amount_paise FROM postings ORDER BY id", MapPosting);

    public static IReadOnlyList<PostingRecord> PostingsOf(SqliteConnection c, long voucherId) =>
        Sql.Query(c, "SELECT voucher_id, date, ledger_id, amount_paise FROM postings WHERE voucher_id = $id ORDER BY id",
            MapPosting, ("$id", voucherId));

    static PostingRecord MapPosting(SqliteDataReader r) =>
        new(r.GetInt64(0), Dates.Parse(r.GetString(1)), new LedgerId(r.GetInt32(2)), new Money(r.GetInt64(3)));

    static void WriteDetails(SqliteTransaction tx, long id, VoucherRow v)
    {
        foreach (var p in v.Postings)
            Sql.Execute(tx, "INSERT INTO postings(voucher_id, date, ledger_id, amount_paise) VALUES ($v, $date, $ledger, $amount)",
                ("$v", id), ("$date", Dates.Format(v.Date)), ("$ledger", p.Ledger.Value), ("$amount", p.Amount.Paise));
        BillRepository.WriteAllocations(tx, id, v.Allocations);

        if (v.Invoice is not { } inv) return;
        Sql.Execute(tx, """
            INSERT INTO invoices(voucher_id, party_ledger_id, direction, kind, place_of_supply, reverse_charge,
                party_doc_number, party_doc_date, original_number, original_date, round_off_paise,
                port_code, shipping_bill_number, shipping_bill_date)
            VALUES ($v, $party, $dir, $kind, $pos, $rc, $pdn, $pdd, $on, $od, $ro, $port, $sbn, $sbd)
            """,
            ("$v", id), ("$party", inv.Party.Value), ("$dir", inv.Direction.ToString()), ("$kind", inv.Kind.ToString()),
            ("$pos", inv.PlaceOfSupply.Value), ("$rc", inv.ReverseCharge ? 1 : 0), ("$pdn", inv.PartyDocNumber),
            ("$pdd", Dates.Format(inv.PartyDocDate)), ("$on", inv.OriginalNumber), ("$od", Dates.Format(inv.OriginalDate)),
            ("$ro", inv.RoundOff.Paise), ("$port", inv.Export?.PortCode), ("$sbn", inv.Export?.ShippingBillNumber),
            ("$sbd", Dates.Format(inv.Export?.ShippingBillDate)));
        for (var i = 0; i < inv.Lines.Count; i++)
        {
            var l = inv.Lines[i];
            Sql.Execute(tx, """
                INSERT INTO invoice_lines(voucher_id, line_no, item_id, ledger_id, description, hsn_sac, qty_thousandths,
                    unit_price_paise, discount_paise, rate_bp, cess_bp, price_includes_tax,
                    taxable_paise, cgst_paise, sgst_paise, utgst_paise, igst_paise, cess_paise)
                VALUES ($v, $n, $item, $ledger, $desc, $hsn, $qty, $price, $disc, $rate, $cessRate, $incl,
                    $taxable, $cgst, $sgst, $utgst, $igst, $cess)
                """,
                ("$v", id), ("$n", i + 1), ("$item", l.ItemId), ("$ledger", l.Ledger.Value), ("$desc", l.Description),
                ("$hsn", l.HsnSac), ("$qty", l.Quantity.Thousandths), ("$price", l.UnitPrice.Paise), ("$disc", l.Discount.Paise),
                ("$rate", l.Rate.BasisPoints), ("$cessRate", l.CessRate.BasisPoints), ("$incl", l.PriceIncludesTax ? 1 : 0),
                ("$taxable", l.Taxable.Paise), ("$cgst", l.Cgst.Paise), ("$sgst", l.Sgst.Paise), ("$utgst", l.Utgst.Paise),
                ("$igst", l.Igst.Paise), ("$cess", l.Cess.Paise));
        }
    }

    static void DeleteDetails(SqliteTransaction tx, long id)
    {
        Sql.Execute(tx, "DELETE FROM postings WHERE voucher_id = $id", ("$id", id));
        Sql.Execute(tx, "DELETE FROM invoice_lines WHERE voucher_id = $id", ("$id", id));
        Sql.Execute(tx, "DELETE FROM invoices WHERE voucher_id = $id", ("$id", id));
        BillRepository.DeleteAllocations(tx, id);
    }

    // Loads vouchers matching a filter on the vouchers table, with their postings, invoices, and allocations, in five queries.
    static IReadOnlyList<VoucherRow> Load(SqliteConnection c, string where, params (string, object?)[] args)
    {
        var ids = $"SELECT id FROM vouchers WHERE {where}";
        var postings = Sql.Query(c, $"SELECT voucher_id, ledger_id, amount_paise FROM postings WHERE voucher_id IN ({ids}) ORDER BY id",
                r => (Voucher: r.GetInt64(0), Line: new PostingLine(new LedgerId(r.GetInt32(1)), new Money(r.GetInt64(2)))), args)
            .ToLookup(x => x.Voucher, x => x.Line);
        var lines = Sql.Query(c, $"""
                SELECT voucher_id, item_id, ledger_id, description, hsn_sac, qty_thousandths, unit_price_paise, discount_paise,
                    rate_bp, cess_bp, price_includes_tax, taxable_paise, cgst_paise, sgst_paise, utgst_paise, igst_paise, cess_paise
                FROM invoice_lines WHERE voucher_id IN ({ids}) ORDER BY voucher_id, line_no
                """,
                r => (Voucher: r.GetInt64(0), Line: new InvoiceLineRow(
                    r.IsDBNull(1) ? null : r.GetInt32(1), new LedgerId(r.GetInt32(2)), r.GetString(3), r.GetString(4),
                    new Quantity(r.GetInt64(5)), new Money(r.GetInt64(6)), new Money(r.GetInt64(7)),
                    new GstRate(r.GetInt32(8)), new GstRate(r.GetInt32(9)), r.GetInt64(10) != 0,
                    new Money(r.GetInt64(11)), new Money(r.GetInt64(12)), new Money(r.GetInt64(13)), new Money(r.GetInt64(14)),
                    new Money(r.GetInt64(15)), new Money(r.GetInt64(16)))), args)
            .ToLookup(x => x.Voucher, x => x.Line);
        var invoices = Sql.Query(c, $"""
                SELECT voucher_id, party_ledger_id, direction, kind, place_of_supply, reverse_charge,
                    party_doc_number, party_doc_date, original_number, original_date, round_off_paise,
                    port_code, shipping_bill_number, shipping_bill_date
                FROM invoices WHERE voucher_id IN ({ids})
                """,
                r => (Voucher: r.GetInt64(0), Header: new InvoiceHeader(
                    new LedgerId(r.GetInt32(1)), Enum.Parse<TradeDirection>(r.GetString(2)), Enum.Parse<InvoiceKind>(r.GetString(3)),
                    new StateCode(r.GetString(4)), r.GetInt64(5) != 0, r.NullableString(6), Dates.ParseNullable(r.NullableString(7)),
                    r.NullableString(8), Dates.ParseNullable(r.NullableString(9)), new Money(r.GetInt64(10)), [],
                    r.IsDBNull(11) && r.IsDBNull(12) && r.IsDBNull(13) ? null
                        : new ExportDetails(r.NullableString(11), r.NullableString(12), Dates.ParseNullable(r.NullableString(13))))), args)
            .ToDictionary(x => x.Voucher, x => x.Header with { Lines = [.. lines[x.Voucher]] });
        var allocations = BillRepository.AllocationsWhere(c, ids, args).ToLookup(x => x.Voucher, x => x.Row);

        return Sql.Query(c, $"SELECT {Columns} FROM vouchers WHERE {where} ORDER BY date, id",
            r =>
            {
                var id = r.GetInt64(0);
                return new VoucherRow(
                    id, Enum.Parse<VoucherType>(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetInt64(4), r.GetString(5),
                    Dates.Parse(r.GetString(6)), r.GetString(7), r.GetInt64(8) != 0, r.GetInt32(9), r.GetInt64(10),
                    [.. postings[id]], invoices.GetValueOrDefault(id), [.. allocations[id]], r.NullableString(11));
            }, args);
    }
}
