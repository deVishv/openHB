using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data.Audit;

namespace OpenHisabKitab.Data.HostDb;

/// <summary>Each company's newest audit record, copied into host.db after every audited company write.</summary>
public static class AuditAnchorRepository
{
    public static AuditHead? Get(SqliteConnection c, int company) =>
        Sql.Single(c, "SELECT seq, hash FROM audit_anchors WHERE company_id = $id", r => new AuditHead(r.GetInt64(0), r.GetString(1)),
            ("$id", company));

    public static void Set(SqliteTransaction tx, int company, AuditHead head) =>
        Sql.Execute(tx, """
            INSERT INTO audit_anchors(company_id, seq, hash) VALUES ($id, $seq, $hash)
            ON CONFLICT(company_id) DO UPDATE SET seq = excluded.seq, hash = excluded.hash
            """, ("$id", company), ("$seq", head.Seq), ("$hash", head.Hash));
}
