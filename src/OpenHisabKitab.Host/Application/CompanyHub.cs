using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Data.Migrations;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Opens company files and runs their writes one at a time. A write is one transaction; its after commit step runs
/// while the company is still locked, so cache updates and change events keep commit order.
/// </summary>
public sealed class CompanyHub(HostData data, TimeProvider time, ILogger<CompanyHub>? log = null)
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
        if (AuditLog.Head(c) is { } head) QueueAnchor(companyId, head, reset: false);
        return result;
    }

    // Anchors waiting to be written to host.db, newest head per company. Saves never wait for host.db: one background
    // writer stores every waiting anchor in one transaction. An anchor a little behind is fine, since verification
    // accepts records after the anchor.
    readonly ConcurrentDictionary<int, (AuditHead Head, bool Reset)> pendingAnchors = new();
    readonly SemaphoreSlim anchorSignal = new(0);
    readonly SemaphoreSlim flushing = new(1, 1);
    readonly Lock writerStart = new();
    Task? anchorWriter;

    /// <summary>
    /// Queues the company's head for its anchor. A reset (after a restore whose own anchor write failed) is set outright
    /// once the file holds it; otherwise <see cref="AuditLog.MayMoveAnchor"/> decides.
    /// </summary>
    internal void QueueAnchor(int companyId, AuditHead head, bool reset)
    {
        pendingAnchors.AddOrUpdate(companyId, (head, reset), (_, old) => (head, reset || old.Reset));
        lock (writerStart) anchorWriter ??= Task.Run(WriteAnchorsLoopAsync);
        anchorSignal.Release();
    }

    /// <summary>A restore drops the old file's waiting anchor before setting the restored one.</summary>
    internal void DropPendingAnchor(int companyId) => pendingAnchors.TryRemove(companyId, out _);

    async Task WriteAnchorsLoopAsync()
    {
        while (true)
        {
            await anchorSignal.WaitAsync().ConfigureAwait(false);
            await FlushAnchorsAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes every waiting anchor now. Verification calls it first, so it judges the newest anchors. If host.db cannot
    /// take them, they wait for the next save and the failure is logged; a committed save is never affected.
    /// </summary>
    public async Task FlushAnchorsAsync()
    {
        await flushing.WaitAsync().ConfigureAwait(false);
        var batch = new Dictionary<int, (AuditHead Head, bool Reset)>();
        var files = new Dictionary<int, SqliteConnection>();
        try
        {
            foreach (var id in pendingAnchors.Keys)
                if (pendingAnchors.TryRemove(id, out var entry)) batch[id] = entry;
            if (batch.Count == 0) return;
            foreach (var id in batch.Keys) files[id] = OpenFile(id);
            await data.WriteHostAsync(tx =>
            {
                foreach (var (id, (head, reset)) in batch)
                {
                    var c = files[id];
                    var stored = AuditAnchorRepository.Get(tx.Connection!, id);
                    var move = reset ? AuditLog.Holds(c, head) : stored is not null && AuditLog.MayMoveAnchor(c, stored, head);
                    if (move) AuditAnchorRepository.Set(tx, id, head);
                }
                return 0;
            }).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A newer head queued meanwhile wins; otherwise the entry waits for the next save.
            foreach (var (id, entry) in batch) pendingAnchors.TryAdd(id, entry);
            log?.LogWarning(e, "Could not update audit anchors in host.db. They are written with the next save.");
        }
        finally
        {
            foreach (var c in files.Values) c.Dispose();
            flushing.Release();
        }
    }

    /// <summary>
    /// Sets the company's anchor to its file's newest audit record, inside the caller's host.db transaction. Only company
    /// creation and restore use this, because it may move the anchor back.
    /// </summary>
    internal static void Anchor(SqliteTransaction hostTx, int companyId, SqliteConnection company)
    {
        if (AuditLog.Head(company) is { } head) AuditAnchorRepository.Set(hostTx, companyId, head);
    }
}
