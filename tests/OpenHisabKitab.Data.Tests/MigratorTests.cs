using static OpenHisabKitab.Data.Tests.TempDb;

namespace OpenHisabKitab.Data.Tests;

public class MigratorTests
{
    [Theory]
    [InlineData(Schema.Host)]
    [InlineData(Schema.Company)]
    public void New_file_migrates_to_latest(Schema s)
    {
        using var db = new TempDb();
        using var c = db.Open();
        var r = Migrator.Migrate(c, s);
        Assert.Equal(new MigrationResult(0, Migrator.Latest(s)), r);
        Assert.True(Migrator.Latest(s) >= 1);
        Assert.Equal((long)Migrator.Latest(s), Scalar(c, "PRAGMA user_version"));
    }

    [Fact]
    public void Host_version_2_gains_user_settings_and_keeps_its_users()
    {
        using var db = new TempDb();
        using var c = db.Open();
        Migrator.Migrate(c, Schema.Host);
        Exec(c, "INSERT INTO users(name, password_hash, role) VALUES ('owner', 'x', 'Owner'); DROP TABLE user_settings; PRAGMA user_version = 2;");
        var from = 0;
        Assert.Equal(new MigrationResult(2, 3), Migrator.Migrate(c, Schema.Host, v => from = v));
        Assert.Equal(2, from);
        Assert.Equal(1L, Scalar(c, "SELECT count(*) FROM users WHERE name = 'owner'"));
        Assert.Equal(0L, Scalar(c, "SELECT count(*) FROM user_settings"));
    }

    [Fact]
    public void Version_1_company_file_migrates_to_2_keeping_its_vouchers()
    {
        using var db = new TempDb();
        using var c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        // Back to version 1: undo what 0002 adds.
        Exec(c, """
            DROP TABLE allocations; DROP TABLE opening_bills; DROP INDEX vouchers_request_id; DROP INDEX invoices_party;
            ALTER TABLE vouchers DROP COLUMN request_id; ALTER TABLE parties DROP COLUMN opening_bills_version;
            ALTER TABLE invoices DROP COLUMN port_code; ALTER TABLE invoices DROP COLUMN shipping_bill_number;
            ALTER TABLE invoices DROP COLUMN shipping_bill_date;
            INSERT INTO vouchers(type, series, fy_start, number, display_number, date, narration, created_by)
                VALUES ('Receipt', 'Main', 2026, 1, 'RCT/26-27/00001', '2026-05-10', 'kept', 1);
            PRAGMA user_version = 1;
            """);
        Assert.Equal(new MigrationResult(1, Migrator.Latest(Schema.Company)), Migrator.Migrate(c, Schema.Company));
        Assert.Equal("kept", Scalar(c, "SELECT narration FROM vouchers WHERE number = 1"));
        Assert.Null(Scalar(c, "SELECT request_id FROM vouchers WHERE number = 1") is DBNull ? null : "set");
        Assert.Equal(0L, Scalar(c, "SELECT count(*) FROM allocations"));
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Second_run_does_nothing()
    {
        using var db = new TempDb();
        using var c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        var called = false;
        var r = Migrator.Migrate(c, Schema.Company, _ => called = true);
        Assert.Equal(r.From, r.To);
        Assert.False(called);
    }

    [Fact]
    public void Before_migrating_is_not_called_for_a_new_file()
    {
        using var db = new TempDb();
        using var c = db.Open();
        var called = false;
        Migrator.Migrate(c, Schema.Company, _ => called = true);
        Assert.False(called);
    }

    [Fact]
    public void Pragmas_are_set()
    {
        using var db = new TempDb();
        using var c = db.Open();
        Assert.Equal("wal", Scalar(c, "PRAGMA journal_mode"));
        Assert.Equal(1L, Scalar(c, "PRAGMA foreign_keys"));
        Assert.Equal(2L, Scalar(c, "PRAGMA synchronous"));
        Assert.Equal(5000L, Scalar(c, "PRAGMA busy_timeout"));
    }

    [Fact]
    public void Second_writer_waits_then_fails_busy()
    {
        using var db = new TempDb();
        using var a = db.Open();
        Exec(a, "CREATE TABLE t(x INTEGER)");
        using var b = db.Open(busySeconds: 1);
        using var tx = SqliteDatabase.BeginWrite(a);
        var started = System.Diagnostics.Stopwatch.StartNew();
        var ex = Assert.Throws<SqliteException>(() => SqliteDatabase.BeginWrite(b));
        Assert.Equal(5, ex.SqliteErrorCode);
        Assert.InRange(started.Elapsed.TotalSeconds, 0.5, 5);
    }

    [Fact]
    public void Newer_file_is_refused_and_left_unchanged()
    {
        using var db = new TempDb();
        // A plain rollback journal file, as a newer version might leave it: opening it with our pragmas would change its header.
        using (var raw = new SqliteConnection($"Data Source={db.Path};Pooling=False"))
        {
            raw.Open();
            Exec(raw, "PRAGMA user_version = 999; CREATE TABLE t(x INTEGER);");
        }
        var before = File.ReadAllBytes(db.Path);
        var ex = Assert.Throws<DatabaseTooNewException>(() => Migrator.CheckNotTooNew(db.Path, Schema.Company));
        Assert.Equal("This file needs a newer Open Hisab Kitab.", ex.Message);
        Assert.Equal(before, File.ReadAllBytes(db.Path));
        Assert.False(File.Exists(db.Path + "-wal"));
    }

    [Fact]
    public void Current_or_missing_file_passes_the_check()
    {
        using var db = new TempDb();
        Migrator.CheckNotTooNew(db.Path, Schema.Company);
        Assert.False(File.Exists(db.Path));
        using (var c = db.Open()) Migrator.Migrate(c, Schema.Company);
        Migrator.CheckNotTooNew(db.Path, Schema.Company);
    }

    [Theory]
    [InlineData(Schema.Host)]
    [InlineData(Schema.Company)]
    public void No_floating_point_columns(Schema s)
    {
        using var db = new TempDb();
        using var c = db.Open();
        Migrator.Migrate(c, s);
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT m.name || '.' || p.name, upper(p.type)
            FROM sqlite_schema m JOIN pragma_table_info(m.name) p
            WHERE m.type = 'table'
            """;
        using var r = cmd.ExecuteReader();
        var bad = new List<string>();
        while (r.Read())
        {
            var type = r.GetString(1);
            if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB")) bad.Add(r.GetString(0));
        }
        Assert.Empty(bad);
    }
}
