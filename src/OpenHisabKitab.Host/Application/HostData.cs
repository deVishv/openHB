using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Data.Migrations;

namespace OpenHisabKitab.Host.Application;

/// <summary>Where the host keeps its files, and access to host.db. Writes to host.db run one at a time.</summary>
public sealed class HostData(string dataDirectory)
{
    readonly Lock hostWrite = new();
    bool migrated;

    public string DataDirectory { get; } = dataDirectory;

    public string HostDbPath => Path.Combine(DataDirectory, "host.db");

    public string CompanyFolder => Path.Combine(DataDirectory, "Companies");

    public string CompanyPath(int id) =>
        Path.Combine(CompanyFolder, string.Create(CultureInfo.InvariantCulture, $"company-{id}.db"));

    /// <summary>The backup folder: host setting backup.folder when set, otherwise Backups in the data folder.</summary>
    public string BackupFolder
    {
        get
        {
            using var c = OpenHost();
            return HostSettingsRepository.Get(c, "backup.folder") is { } json && JsonSerializer.Deserialize<string>(json) is { Length: > 0 } folder
                ? folder
                : Path.Combine(DataDirectory, "Backups");
        }
    }

    /// <summary>Opens host.db, creating and migrating it the first time in this process.</summary>
    public SqliteConnection OpenHost()
    {
        if (!migrated)
        {
            lock (hostWrite)
            {
                if (!migrated)
                {
                    Directory.CreateDirectory(DataDirectory);
                    DataFolderSecurity.Restrict(DataDirectory);
                    Migrator.CheckNotTooNew(HostDbPath, Schema.Host);
                    using var first = SqliteDatabase.Open(HostDbPath);
                    Migrator.Migrate(first, Schema.Host,
                        _ => new BackupManager(TimeProvider.System).Backup(HostDbPath, FolderFrom(first)));
                    migrated = true;
                }
            }
        }
        return SqliteDatabase.Open(HostDbPath);
    }

    // The backup folder read through an already open connection, for use while host.db is being opened.
    string FolderFrom(SqliteConnection c) =>
        HostSettingsRepository.Get(c, "backup.folder") is { } json && JsonSerializer.Deserialize<string>(json) is { Length: > 0 } folder
            ? folder
            : Path.Combine(DataDirectory, "Backups");

    public T WriteHost<T>(Func<SqliteTransaction, T> work)
    {
        lock (hostWrite)
        {
            using var c = OpenHost();
            using var tx = SqliteDatabase.BeginWrite(c);
            var result = work(tx);
            tx.Commit();
            return result;
        }
    }
}
