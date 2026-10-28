using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.Audit;

/// <summary>
/// The append only audit trail (spec 5.5). Each row's hash covers the previous row's hash, so editing or removing
/// any row except the last breaks the chain from that row on. A company trail is also checked against its anchor, a
/// copy of its head kept in host.db, which catches removed newest rows and a rebuilt chain.
/// </summary>
public static class AuditLog
{
    static readonly string Genesis = new('0', 64);

    public static long Append(SqliteTransaction tx, AuditEntry e)
    {
        var c = tx.Connection!;
        long seq;
        string prev;
        using (var last = c.CreateCommand())
        {
            last.Transaction = tx;
            last.CommandText = "SELECT seq, hash FROM audit_log ORDER BY seq DESC LIMIT 1";
            using var r = last.ExecuteReader();
            (seq, prev) = r.Read() ? (r.GetInt64(0) + 1, r.GetString(1)) : (1L, Genesis);
        }

        var at = FormatAt(e.At);
        var hash = Hash(prev, seq, e, at);
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO audit_log(seq, at, user, machine, source, token_name, action, entity, entity_key, before, after, prev_hash, hash)
            VALUES ($seq, $at, $user, $machine, $source, $token, $action, $entity, $key, $before, $after, $prev, $hash)
            """;
        cmd.Parameters.AddWithValue("$seq", seq);
        cmd.Parameters.AddWithValue("$at", at);
        cmd.Parameters.AddWithValue("$user", e.User);
        cmd.Parameters.AddWithValue("$machine", e.Machine);
        cmd.Parameters.AddWithValue("$source", e.Source.ToString());
        cmd.Parameters.AddWithValue("$token", (object?)e.TokenName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$action", e.Action);
        cmd.Parameters.AddWithValue("$entity", e.Entity);
        cmd.Parameters.AddWithValue("$key", e.EntityKey);
        cmd.Parameters.AddWithValue("$before", (object?)e.Before ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$after", (object?)e.After ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$prev", prev);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.ExecuteNonQuery();
        return seq;
    }

    /// <summary>The newest record, or null for an empty trail.</summary>
    public static AuditHead? Head(SqliteConnection c) =>
        Sql.Single(c, "SELECT seq, hash FROM audit_log ORDER BY seq DESC LIMIT 1", r => new AuditHead(r.GetInt64(0), r.GetString(1)));

    /// <summary>True when the trail still has this record with this hash.</summary>
    public static bool Holds(SqliteConnection c, AuditHead record) =>
        Sql.Single(c, "SELECT hash FROM audit_log WHERE seq = $seq", r => r.GetString(0), ("$seq", record.Seq)) == record.Hash;

    /// <summary>
    /// The anchor rule for company and host trails alike: an anchor moves from one record to a newer one only while the
    /// trail still holds both. After records were removed or the chain rebuilt it stays at the last good record, so
    /// verification keeps reporting the change. Only company creation and restore set an anchor outright.
    /// </summary>
    public static bool MayMoveAnchor(SqliteConnection c, AuditHead from, AuditHead to) =>
        to.Seq >= from.Seq && Holds(c, from) && Holds(c, to);

    /// <summary>Checks the chain alone. Use the anchored overload for company files.</summary>
    public static AuditVerification Verify(SqliteConnection c) => Verify(c, null, anchored: false);

    /// <summary>
    /// Checks the chain and that it still holds the anchored record. Records after the anchor are fine: the anchor is
    /// written just after the company commit, so a crash between the two leaves it one step behind.
    /// </summary>
    public static AuditVerification Verify(SqliteConnection c, AuditHead? anchor) => Verify(c, anchor, anchored: true);

    static AuditVerification Verify(SqliteConnection c, AuditHead? anchor, bool anchored)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM audit_log ORDER BY seq";
        using var r = cmd.ExecuteReader();
        long count = 0, expectedSeq = 1;
        var prev = Genesis;
        while (r.Read())
        {
            count++;
            var seq = r.GetInt64(0);
            // The hash is checked on the raw stored text, so a row too damaged to parse is reported, not thrown.
            var ok = seq == expectedSeq && r.GetString(11) == prev && RawHash(prev, r) == r.GetString(12);
            if (!ok) return new AuditVerification(count, seq, Anchor(c, anchored, anchor));
            prev = r.GetString(12);
            expectedSeq++;
        }
        return new AuditVerification(count, null, Anchor(c, anchored, anchor));
    }

    // Judged on its own, so a broken row elsewhere in the chain does not hide whether the anchored record is intact.
    static AnchorCheck Anchor(SqliteConnection c, bool anchored, AuditHead? anchor) =>
        !anchored ? AnchorCheck.NotUsed
        : anchor is null ? AnchorCheck.Missing
        : Holds(c, anchor) ? AnchorCheck.Matches
        : AnchorCheck.Mismatch;

    /// <summary>Newest first, at most <see cref="AuditFilter.Limit"/> rows.</summary>
    public static IReadOnlyList<StoredAuditEntry> Read(SqliteConnection c, AuditFilter f)
    {
        using var cmd = c.CreateCommand();
        var where = new List<string>();
        if (f.From is { } from) { where.Add("at >= $from"); cmd.Parameters.AddWithValue("$from", FormatAt(from)); }
        if (f.To is { } to) { where.Add("at <= $to"); cmd.Parameters.AddWithValue("$to", FormatAt(to)); }
        if (f.User is { } user) { where.Add("user = $user COLLATE NOCASE"); cmd.Parameters.AddWithValue("$user", user); }
        if (f.Entity is { } entity) { where.Add("entity = $entity"); cmd.Parameters.AddWithValue("$entity", entity); }
        cmd.CommandText = $"SELECT {Columns} FROM audit_log"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY seq DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", f.Limit);
        using var r = cmd.ExecuteReader();
        var rows = new List<StoredAuditEntry>();
        while (r.Read()) rows.Add(ReadRow(r));
        return rows;
    }

    const string Columns = "seq, at, user, machine, source, token_name, action, entity, entity_key, before, after, prev_hash, hash";

    static StoredAuditEntry ReadRow(SqliteDataReader r) => new(
        r.GetInt64(0),
        new AuditEntry(
            DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            r.GetString(2), r.GetString(3), Enum.Parse<ChangeSource>(r.GetString(4)),
            r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10)),
        r.GetString(11), r.GetString(12));

    // Stored in UTC so text order is time order.
    static string FormatAt(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    static string Hash(string prev, long seq, AuditEntry e, string at) =>
        Hash(prev, seq, at, e.User, e.Machine, e.Source.ToString(), e.TokenName, e.Action, e.Entity, e.EntityKey, e.Before, e.After);

    static string RawHash(string prev, SqliteDataReader r) =>
        Hash(prev, r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.NullableString(5),
            r.GetString(6), r.GetString(7), r.GetString(8), r.NullableString(9), r.NullableString(10));

    // The hash covers the stored text of every column, so verification never depends on re-parsing it.
    static string Hash(string prev, long seq, string at, string user, string machine, string source, string? tokenName,
        string action, string entity, string entityKey, string? before, string? after)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            seq, at, user, machine, source, tokenName, action, entity, entityKey, before, after,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prev + "\n" + canonical)));
    }
}
