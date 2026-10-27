using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data;

/// <summary>Opens SQLite files with the settings every Open Hisab Kitab database needs.</summary>
public static class SqliteDatabase
{
    /// <summary>Opens a file. A writer waits up to <paramref name="busySeconds"/> for another writer before failing.</summary>
    public static SqliteConnection Open(string path, int busySeconds = 5)
    {
        // DefaultTimeout is Microsoft.Data.Sqlite's own busy retry; it must match busy_timeout or it silently extends the wait.
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = busySeconds,
        };
        var c = new SqliteConnection(builder.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        // synchronous=FULL: a power cut may lose nothing that was reported as saved.
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL; PRAGMA busy_timeout = " + (busySeconds * 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + ";";
        cmd.ExecuteNonQuery();
        return c;
    }

    /// <summary>Starts a write transaction that takes the write lock at once, so two writers queue instead of failing mid way.</summary>
    public static SqliteTransaction BeginWrite(SqliteConnection c) => c.BeginTransaction(deferred: false);
}
