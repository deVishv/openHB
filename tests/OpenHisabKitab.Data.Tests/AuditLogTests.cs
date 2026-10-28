using OpenHisabKitab.Data.Audit;
using static OpenHisabKitab.Data.Tests.TempDb;

namespace OpenHisabKitab.Data.Tests;

public class AuditLogTests
{
    static readonly DateTimeOffset At = new(2026, 10, 27, 9, 30, 0, TimeSpan.FromHours(5.5));

    static AuditEntry Entry(int n) =>
        new(At.AddMinutes(n), "owner", "PC1", ChangeSource.App, null, "create", "voucher", n.ToString(CultureInfo.InvariantCulture),
            null, $$"""{"amount":"{{n}}00.00"}""");

    static SqliteConnection Migrated(TempDb db)
    {
        var c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        return c;
    }

    static void AppendAll(SqliteConnection c, int count)
    {
        using var tx = SqliteDatabase.BeginWrite(c);
        for (var i = 1; i <= count; i++) AuditLog.Append(tx, Entry(i));
        tx.Commit();
    }

    [Fact]
    public void Empty_log_verifies()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        var v = AuditLog.Verify(c);
        Assert.Equal(0, v.Count);
        Assert.True(v.Ok);
    }

    [Fact]
    public void Host_schema_has_the_log_too()
    {
        using var db = new TempDb();
        using var c = db.Open();
        Migrator.Migrate(c, Schema.Host);
        AppendAll(c, 1);
        Assert.True(AuditLog.Verify(c).Ok);
    }

    [Fact]
    public void Three_entries_verify_and_chain()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        var v = AuditLog.Verify(c);
        Assert.Equal(3, v.Count);
        Assert.True(v.Ok);
        var rows = AuditLog.Read(c, new AuditFilter());
        Assert.Equal(new long[] { 1, 2, 3 }, rows.Select(r => r.Seq).Order());
        var bySeq = rows.ToDictionary(r => r.Seq);
        Assert.Equal(new string('0', 64), bySeq[1].PrevHash);
        Assert.Equal(bySeq[1].Hash, bySeq[2].PrevHash);
        Assert.Equal(Entry(2), bySeq[2].Entry with { At = bySeq[2].Entry.At.ToOffset(At.Offset) });
    }

    [Fact]
    public void Edited_row_is_detected()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        Exec(c, "DROP TRIGGER audit_log_no_update; UPDATE audit_log SET after = '{}' WHERE seq = 2;");
        Assert.Equal(2, AuditLog.Verify(c).FirstBadSeq);
    }

    [Fact]
    public void Deleted_middle_row_is_detected()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        Exec(c, "DROP TRIGGER audit_log_no_delete; DELETE FROM audit_log WHERE seq = 2;");
        Assert.Equal(3, AuditLog.Verify(c).FirstBadSeq);
    }

    [Fact]
    public void Deleted_last_row_breaks_nothing_detectable_but_count_drops()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        Exec(c, "DROP TRIGGER audit_log_no_delete; DELETE FROM audit_log WHERE seq = 3;");
        Assert.Equal(2, AuditLog.Verify(c).Count);
    }

    [Fact]
    public void Update_is_blocked_by_trigger()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 1);
        var ex = Assert.Throws<SqliteException>(() => Exec(c, "UPDATE audit_log SET user = 'x'"));
        Assert.Contains("append only", ex.Message);
    }

    [Fact]
    public void Delete_is_blocked_by_trigger()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 1);
        Assert.Throws<SqliteException>(() => Exec(c, "DELETE FROM audit_log"));
    }

    [Fact]
    public void Rolled_back_append_leaves_nothing()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            AuditLog.Append(tx, Entry(1));
            tx.Rollback();
        }
        Assert.Equal(0, AuditLog.Verify(c).Count);
    }

    [Fact]
    public void Hash_does_not_depend_on_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        var hashes = new List<string>();
        try
        {
            foreach (var culture in new[] { "hi-IN", "de-DE", "ar-SA" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                using var db = new TempDb();
                using var c = Migrated(db);
                AppendAll(c, 2);
                hashes.Add(AuditLog.Read(c, new AuditFilter()).Single(r => r.Seq == 2).Hash);
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
        Assert.Single(hashes.Distinct());
    }

    [Fact]
    public void Read_filters_by_user_and_entity()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            AuditLog.Append(tx, Entry(1));
            AuditLog.Append(tx, Entry(2) with { User = "asha", Entity = "ledger" });
            tx.Commit();
        }
        Assert.Equal(2, AuditLog.Read(c, new AuditFilter(User: "asha")).Single().Seq);
        Assert.Equal(1, AuditLog.Read(c, new AuditFilter(Entity: "voucher")).Single().Seq);
    }

    [Fact]
    public void Token_name_and_source_round_trip()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            AuditLog.Append(tx, Entry(1) with { Source = ChangeSource.Mcp, TokenName = "claude" });
            tx.Commit();
        }
        var e = AuditLog.Read(c, new AuditFilter()).Single().Entry;
        Assert.Equal(ChangeSource.Mcp, e.Source);
        Assert.Equal("claude", e.TokenName);
    }

    [Fact]
    public void Replace_of_existing_row_is_blocked()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 2);
        var ex = Assert.Throws<SqliteException>(() => Exec(c, """
            INSERT OR REPLACE INTO audit_log(seq, at, user, machine, source, action, entity, entity_key, prev_hash, hash)
            SELECT seq, at, 'x', machine, source, action, entity, entity_key, prev_hash, hash FROM audit_log WHERE seq = 1
            """));
        Assert.Contains("append only", ex.Message);
        Assert.Equal("owner", AuditLog.Read(c, new AuditFilter()).Single(r => r.Seq == 1).Entry.User);
    }

    [Fact]
    public void Insert_not_chained_to_the_head_is_blocked()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 1);
        Assert.Throws<SqliteException>(() => Exec(c, """
            INSERT INTO audit_log(seq, at, user, machine, source, action, entity, entity_key, prev_hash, hash)
            VALUES (2, 'x', 'u', 'm', 'App', 'a', 'e', 'k', 'not the head', 'h')
            """));
        Assert.Throws<SqliteException>(() => Exec(c, """
            INSERT INTO audit_log(seq, at, user, machine, source, action, entity, entity_key, prev_hash, hash)
            SELECT 5, 'x', 'u', 'm', 'App', 'a', 'e', 'k', hash, 'h' FROM audit_log WHERE seq = 1
            """));
    }

    [Fact]
    public void Unreadable_row_is_reported_not_thrown()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        Exec(c, "DROP TRIGGER audit_log_no_update; UPDATE audit_log SET at = 'garbage', source = 'Nope' WHERE seq = 2;");
        Assert.Equal(2, AuditLog.Verify(c).FirstBadSeq);
    }

    [Fact]
    public void Anchor_at_the_head_matches_and_a_lagging_anchor_still_matches()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 2);
        var two = AuditLog.Head(c)!;
        Assert.Equal(2, two.Seq);
        Assert.Equal(AnchorCheck.Matches, AuditLog.Verify(c, two).Anchor);

        AppendAll(c, 1);
        var lagging = AuditLog.Verify(c, two);
        Assert.True(lagging.Ok);
        Assert.Equal(AnchorCheck.Matches, lagging.Anchor);
    }

    [Fact]
    public void Removing_the_newest_rows_no_longer_matches_the_anchor()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        var anchor = AuditLog.Head(c)!;
        Exec(c, "DROP TRIGGER audit_log_no_delete; DELETE FROM audit_log WHERE seq >= 2;");
        Assert.True(AuditLog.Verify(c).Ok);
        var v = AuditLog.Verify(c, anchor);
        Assert.False(v.Ok);
        Assert.Equal(AnchorCheck.Mismatch, v.Anchor);
    }

    [Fact]
    public void Rebuilt_chain_no_longer_matches_the_anchor()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        var anchor = AuditLog.Head(c)!;
        Exec(c, "DROP TRIGGER audit_log_no_delete; DELETE FROM audit_log;");
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            for (var i = 1; i <= 3; i++) AuditLog.Append(tx, Entry(i) with { After = "{}" });
            tx.Commit();
        }
        Assert.True(AuditLog.Verify(c).Ok);
        Assert.Equal(AnchorCheck.Mismatch, AuditLog.Verify(c, anchor).Anchor);
    }

    [Fact]
    public void Missing_anchor_fails()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 1);
        var v = AuditLog.Verify(c, null);
        Assert.False(v.Ok);
        Assert.Equal(AnchorCheck.Missing, v.Anchor);
        Assert.Equal(AnchorCheck.NotUsed, AuditLog.Verify(c).Anchor);
    }

    [Fact]
    public void A_broken_row_after_the_anchor_does_not_hide_that_the_anchor_matches()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 2);
        var anchor = AuditLog.Head(c)!;
        AppendAll(c, 2);
        Exec(c, "DROP TRIGGER audit_log_no_update; UPDATE audit_log SET after = '{}' WHERE seq = 4;");
        var v = AuditLog.Verify(c, anchor);
        Assert.Equal(4, v.FirstBadSeq);
        Assert.Equal(AnchorCheck.Matches, v.Anchor);
    }

    [Fact]
    public void Anchor_moves_only_forward_and_only_between_held_records()
    {
        using var db = new TempDb();
        using var c = Migrated(db);
        AppendAll(c, 3);
        var two = new AuditHead(2, AuditLog.Read(c, new AuditFilter()).Single(r => r.Seq == 2).Hash);
        var head = AuditLog.Head(c)!;
        Assert.True(AuditLog.MayMoveAnchor(c, two, head));
        Assert.False(AuditLog.MayMoveAnchor(c, head, two));
        Assert.False(AuditLog.MayMoveAnchor(c, two, head with { Hash = "x" }));
        Assert.False(AuditLog.MayMoveAnchor(c, two with { Hash = "x" }, head));
    }
}
