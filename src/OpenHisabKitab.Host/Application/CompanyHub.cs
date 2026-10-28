using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.Migrations;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Opens company files and runs their writes one at a time. A write is one transaction; its after commit step runs
/// while the company is still locked, so cache updates and change events keep commit order.
/// </summary>
public sealed class CompanyHub(HostData data, TimeProvider time)
{
    readonly ConcurrentDictionary<int, SemaphoreSlim> writeLocks = new();
    readonly ConcurrentDictionary<int, bool> migrated = new();
    readonly ConcurrentDictionary<int, object> migrateLocks = new();

    public MaintenanceGate Gate { get; } = new();

    /// <summary>Test hook: runs before each audit append, so a test can make the append fail.</summary>
    internal Action<AuditEntry>? BeforeAudit { get; set; }

    public SqliteConnection Open(int companyId)
    {
        Gate.Check(companyId);
        return OpenFile(companyId);
    }

    /// <summary>Opens without the maintenance check; only a restore that holds the gate may use this.</summary>
    internal SqliteConnection OpenFile(int companyId)
    {
        var path = data.CompanyPath(companyId);
        if (!File.Exists(path)) throw new ServiceException(ServiceError.NotFound, "No such company.");
        if (!migrated.ContainsKey(companyId))
        {
            // Two first opens at once must not both run the same migration.
            lock (migrateLocks.GetOrAdd(companyId, _ => new object()))
            {
                if (!migrated.ContainsKey(companyId))
                {
                    try { Migrator.CheckNotTooNew(path, Schema.Company); }
                    catch (DatabaseTooNewException e) { throw new ServiceException(ServiceError.TooNew, e.Message); }
                    using var first = SqliteDatabase.Open(path);
                    Migrator.Migrate(first, Schema.Company, _ => new BackupManager(time).Backup(path, data.BackupFolder));
                    migrated[companyId] = true;
                }
            }
        }
        return SqliteDatabase.Open(path);
    }

    /// <summary>Creates and migrates a new company file. Fails if the file already exists.</summary>
    internal SqliteConnection CreateFile(int companyId)
    {
        var path = data.CompanyPath(companyId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) throw new InvalidOperationException($"Company file {path} already exists.");
        var c = SqliteDatabase.Open(path);
        Migrator.Migrate(c, Schema.Company);
        migrated[companyId] = true;
        return c;
    }

    /// <summary>Removes a company file whose creation failed.</summary>
    internal void DeleteFile(int companyId)
    {
        SqliteConnection.ClearAllPools();
        migrated.TryRemove(companyId, out _);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(data.CompanyPath(companyId) + suffix);
    }

    /// <summary>Waiting for the company's write lock frees the thread, so many users saving at once do not tie up the server.</summary>
    public async Task<T> WriteAsync<T>(int companyId, Func<SqliteTransaction, T> work, Action<T>? afterCommit = null)
    {
        var gate = writeLocks.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try { return Run(companyId, work, afterCommit); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Waits for the company's write lock, then closes the gate so no other company work starts. Disposing reopens both.
    /// </summary>
    internal async Task<IDisposable> MaintenanceAsync(int companyId)
    {
        var gate = writeLocks.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        Gate.Close(companyId);
        return new Release(() =>
        {
            Gate.Open(companyId);
            gate.Release();
        });
    }

    sealed class Release(Action release) : IDisposable
    {
        int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0) release();
        }
    }

    /// <summary>Appends to the company's audit trail inside the caller's transaction.</summary>
    public void Audit(SqliteTransaction tx, AuditEntry e)
    {
        BeforeAudit?.Invoke(e);
        AuditLog.Append(tx, e);
    }

    T Run<T>(int companyId, Func<SqliteTransaction, T> work, Action<T>? afterCommit)
    {
        using var c = Open(companyId);
        T result;
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            result = work(tx);
            tx.Commit();
        }
        afterCommit?.Invoke(result);
        return result;
    }
}
