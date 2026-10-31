using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>A customer or supplier. Every party is also a ledger.</summary>
public sealed record Party(LedgerId Ledger, Gstin? Gstin, StateCode State, int CreditDays, string Address, long RowVersion);

public static class PartyRepository
{
    const string Columns = "ledger_id, gstin, state_code, credit_days, address, row_version";

    static Party Map(SqliteDataReader r) => new(
        new LedgerId(r.GetInt32(0)), StoredGst.Parse(r.NullableString(1)), new StateCode(r.GetString(2)), r.GetInt32(3),
        r.GetString(4), r.GetInt64(5));

    public static Party? Get(SqliteConnection c, LedgerId l) =>
        Sql.Single(c, $"SELECT {Columns} FROM parties WHERE ledger_id = $id", Map, ("$id", l.Value));

    public static IReadOnlyList<Party> List(SqliteConnection c) =>
        Sql.Query(c, $"SELECT {Columns} FROM parties ORDER BY ledger_id", Map);

    /// <summary>Parties after the given ledger id, by ledger id, at most limit of them.</summary>
    public static IReadOnlyList<Party> Page(SqliteConnection c, long? afterLedger, int limit) =>
        Sql.Query(c, $"SELECT {Columns} FROM parties WHERE ledger_id > $a ORDER BY ledger_id LIMIT $limit", Map,
            ("$a", afterLedger ?? 0), ("$limit", limit));

    /// <summary>Inserts or updates. The stored row version starts at 1 and goes up by one on each update.</summary>
    public static void Upsert(SqliteTransaction tx, Party p) =>
        Sql.Execute(tx, """
            INSERT INTO parties(ledger_id, gstin, state_code, credit_days, address) VALUES ($id, $gstin, $state, $days, $address)
            ON CONFLICT(ledger_id) DO UPDATE SET gstin = excluded.gstin, state_code = excluded.state_code,
                credit_days = excluded.credit_days, address = excluded.address, row_version = row_version + 1
            """,
            ("$id", p.Ledger.Value), ("$gstin", p.Gstin?.Value), ("$state", p.State.Value), ("$days", p.CreditDays), ("$address", p.Address));
}
