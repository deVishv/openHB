using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Data.Migrations;

namespace OpenHisabKitab.Host.Application;

/// <summary>Where the host keeps its files, and access to host.db. Writes to host.db run one at a time.</summary>
public sealed class HostData(string dataDirectory)
{
    readonly Lock migrateLock = new();
    readonly SemaphoreSlim hostWrite = new(1, 1);
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
            lock (migrateLock)
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
        hostWrite.Wait();
        try { return RunHost(work); }
        finally { hostWrite.Release(); }
    }

    /// <summary>Like WriteHost, but waiting for the host.db write lock frees the thread. Every company save uses it.</summary>
    public async Task<T> WriteHostAsync<T>(Func<SqliteTransaction, T> work)
    {
        await hostWrite.WaitAsync().ConfigureAwait(false);
        try { return RunHost(work); }
        finally { hostWrite.Release(); }
    }

    T RunHost<T>(Func<SqliteTransaction, T> work)
    {
        using var c = OpenHost();
        var before = AuditLog.Head(c);
        T result;
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            result = work(tx);
            tx.Commit();
        }
        if (AuditLog.Head(c) is { } head && head != before) AdvanceHostAnchor(c, before, head);
        return result;
    }

    string HostAnchorPath => Path.Combine(DataDirectory, "host-audit-anchor.json");

    // An anchor this process was allowed to write but could not, so a failed write is retried and does not leave the
    // anchor stuck. Only touched under the host.db write lock.
    AuditHead? unwrittenHostAnchor;

    /// <summary>
    /// The newest record of host.db's own audit trail, kept in a file beside it, so removing host.db's newest records or
    /// rebuilding its chain shows. Null when the file is missing; a record that matches nothing when its content is not
    /// an anchor. A host.db restore (Plan 8) must reset it.
    /// </summary>
    public AuditHead? HostAnchor()
    {
        try { return ReadHostAnchor(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ServiceException(ServiceError.Unavailable, "Could not read the host audit anchor. Try again in a minute.");
        }
    }

    AuditHead? ReadHostAnchor()
    {
        try { return JsonSerializer.Deserialize<AuditHead>(File.ReadAllText(HostAnchorPath)) ?? new AuditHead(0, ""); }
        catch (FileNotFoundException) { return null; }
        catch (JsonException) { return new AuditHead(0, ""); }
    }

    // The anchor rule of AuditLog.MayMoveAnchor, plus: the very first records create it. Runs under the host.db write
    // lock, so it only moves forward. host.db has already committed, so nothing here may fail the caller's write: if the
    // file cannot be read or written, the anchor stays behind, which verification accepts, and the next host.db write
    // tries again.
    void AdvanceHostAnchor(SqliteConnection c, AuditHead? before, AuditHead head)
    {
        try
        {
            var stored = ReadHostAnchor();
            var allowed = (stored is null && before is null)
                || (stored is not null && AuditLog.MayMoveAnchor(c, stored, head))
                || (unwrittenHostAnchor is { } unwritten && AuditLog.MayMoveAnchor(c, unwritten, head));
            if (!allowed) return;
            unwrittenHostAnchor = head;
            // Flushed before the rename, so a power cut leaves the old anchor or the new one, never a torn file.
            var temp = HostAnchorPath + ".tmp";
            using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                f.Write(JsonSerializer.SerializeToUtf8Bytes(head));
                f.Flush(flushToDisk: true);
            }
            File.Move(temp, HostAnchorPath, overwrite: true);
            unwrittenHostAnchor = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
