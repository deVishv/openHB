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
        using (var c = db.Open()) Exec(c, "PRAGMA user_version = 999; CREATE TABLE t(x INTEGER);");
        SqliteConnection.ClearAllPools();
        var before = File.ReadAllBytes(db.Path);
        using (var c = db.Open())
        {
            var ex = Assert.Throws<DatabaseTooNewException>(() => Migrator.Migrate(c, Schema.Company));
            Assert.Equal("This file needs a newer Open Hisab Kitab.", ex.Message);
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(before, File.ReadAllBytes(db.Path));
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
