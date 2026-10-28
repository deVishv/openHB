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

    /// <summary>
    /// Opens without the maintenance check. Only a restore that holds the gate may write through it; the anchor writer
    /// also uses it, to read the newest audit records while a restore runs.
    /// </summary>
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
        Requeue(companyId, (head, reset));
        lock (writerStart) anchorWriter ??= Task.Run(WriteAnchorsLoopAsync);
        anchorSignal.Release();
    }

    // The newer head wins, but a reset is never lost: without it the restored file never matches the old anchor.
    void Requeue(int companyId, (AuditHead Head, bool Reset) entry) =>
        pendingAnchors.AddOrUpdate(companyId, entry, (_, current) =>
            (current.Head.Seq >= entry.Head.Seq ? current.Head : entry.Head, current.Reset || entry.Reset));

    /// <summary>Test hook: runs before the anchors' host.db write, so a test can make it fail.</summary>
    internal Action? BeforeAnchorWrite { get; set; }

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
    /// Writes every waiting anchor now. Verification calls it first, so it judges the newest anchors, and so does host
    /// shutdown. A company whose file cannot be read keeps its entry without holding up the others. If host.db cannot
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
            foreach (var id in batch.Keys)
            {
                try { files[id] = OpenFile(id); }
                catch (Exception e) when (e is not OutOfMemoryException) { Kept(id, batch[id], e); }
            }
            if (files.Count == 0) return;
            try
            {
                BeforeAnchorWrite?.Invoke();
                await data.WriteHostAsync(tx =>
                {
                    foreach (var (id, c) in files)
                    {
                        var (head, reset) = batch[id];
                        // One unreadable company file keeps its own entry; the others are still written.
                        try
                        {
                            var stored = AuditAnchorRepository.Get(tx.Connection!, id);
                            var move = reset ? AuditLog.Holds(c, head) : stored is not null && AuditLog.MayMoveAnchor(c, stored, head);
                            if (move) AuditAnchorRepository.Set(tx, id, head);
                        }
                        catch (SqliteException e) { Kept(id, batch[id], e); }
                    }
                    return 0;
                }).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                foreach (var id in files.Keys) Kept(id, batch[id], e);
            }
        }
        finally
        {
            foreach (var c in files.Values) c.Dispose();
            flushing.Release();
        }
    }

    void Kept(int companyId, (AuditHead Head, bool Reset) entry, Exception e)
    {
        Requeue(companyId, entry);
        log?.LogWarning(e, "Could not update the audit anchor of company {Company}. It is written with the next save.", companyId);
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
