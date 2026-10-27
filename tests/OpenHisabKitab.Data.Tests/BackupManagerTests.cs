using Microsoft.Extensions.Time.Testing;
using OpenHisabKitab.Data.Backup;
using static OpenHisabKitab.Data.Tests.TempDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class BackupManagerTests : IDisposable
{
    readonly TempDb db = new("company-1.db");
    readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 27, 1, 0, 0, TimeSpan.Zero));
    readonly BackupManager manager;
    readonly string folder;

    public BackupManagerTests()
    {
        manager = new BackupManager(time);
        folder = Path.Combine(db.Folder, "Backups");
        using var c = db.Open();
        Exec(c, "CREATE TABLE t(x INTEGER); INSERT INTO t VALUES (1), (2);");
    }

    public void Dispose() => db.Dispose();

    static long Count(string path)
    {
        using var c = SqliteDatabase.Open(path);
        return (long)Scalar(c, "SELECT count(*) FROM t")!;
    }

    [Fact]
    public void Backup_contains_the_rows_and_is_named_by_time()
    {
        var b = manager.Backup(db.Path, folder);
        Assert.Equal(Path.Combine(folder, "company-1-20261027-010000.db"), b.Path);
        Assert.Equal("company-1", b.SourceName);
        Assert.Equal(time.GetUtcNow(), b.TakenAt);
        Assert.Equal(2, Count(b.Path));
    }

    [Fact]
    public void Two_backups_in_one_second_do_not_overwrite()
    {
        var a = manager.Backup(db.Path, folder);
        var b = manager.Backup(db.Path, folder);
        Assert.NotEqual(a.Path, b.Path);
        Assert.Equal(2, manager.List(folder, "company-1").Count);
    }

    [Fact]
    public void List_is_newest_first_and_only_that_source()
    {
        manager.Backup(db.Path, folder);
        time.Advance(TimeSpan.FromDays(1));
        var newest = manager.Backup(db.Path, folder);
        File.WriteAllText(Path.Combine(folder, "company-10-20261030-010000.db"), "");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "");
        var list = manager.List(folder, "company-1");
        Assert.Equal(2, list.Count);
        Assert.Equal(newest.Path, list[0].Path);
    }

    [Fact]
    public void Prune_keeps_the_newest()
    {
        for (var i = 0; i < 32; i++)
        {
            manager.Backup(db.Path, folder);
            time.Advance(TimeSpan.FromDays(1));
        }
        var other = Path.Combine(folder, "company-10-20261030-010000.db");
        File.WriteAllText(other, "");
        var newest = manager.List(folder, "company-1").Take(30).Select(b => b.Path).ToList();
        manager.Prune(folder, "company-1", keep: 30);
        Assert.Equal(newest, manager.List(folder, "company-1").Select(b => b.Path));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Restore_brings_back_the_backup()
    {
        var b = manager.Backup(db.Path, folder);
        using var live = db.Open();
        Exec(live, "INSERT INTO t VALUES (3)");
        manager.RestoreInto(b.Path, live);
        Assert.Equal(2L, Scalar(live, "SELECT count(*) FROM t"));
    }

    [Fact]
    public void Damaged_backup_is_refused_and_live_is_unchanged()
    {
        var b = manager.Backup(db.Path, folder);
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(b.Path);
        File.WriteAllBytes(b.Path, bytes[..(bytes.Length / 2)]);
        using var live = db.Open();
        Exec(live, "INSERT INTO t VALUES (3)");
        Assert.Throws<BackupFailedException>(() => manager.RestoreInto(b.Path, live));
        Assert.Equal(3L, Scalar(live, "SELECT count(*) FROM t"));
    }

    [Fact]
    public void Garbage_backup_is_refused()
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "company-1-20261027-010000.db");
        File.WriteAllText(path, "this is not a database");
        using var live = db.Open();
        Assert.Throws<BackupFailedException>(() => manager.RestoreInto(path, live));
        Assert.Equal(2L, Scalar(live, "SELECT count(*) FROM t"));
    }

    [Fact]
    public void Backup_during_an_open_write_takes_committed_data()
    {
        using var writer = db.Open();
        using var tx = SqliteDatabase.BeginWrite(writer);
        using (var cmd = writer.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO t VALUES (99)";
            cmd.ExecuteNonQuery();
        }
        var b = manager.Backup(db.Path, folder);
        Assert.Equal(2, Count(b.Path));
    }
}
