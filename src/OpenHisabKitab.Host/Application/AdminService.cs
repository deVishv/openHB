using System.Text.Json;
using OpenHisabKitab.Data;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Data.Migrations;

namespace OpenHisabKitab.Host.Application;

/// <summary>Period locks (spec 5.6), the audit trail view (5.5), backups and restore (9).</summary>
public sealed class AdminService(HostData data, CompanyHub hub, SessionStore sessions, ChangeFeed feed, CompanyBooks books, TimeProvider time)
{
    readonly BackupManager backups = new(time);

    /// <summary>Locks the books up to a date. Moving the lock earlier unlocks a period, which needs the unlock right.</summary>
    public Task LockUpToAsync(CallContext ctx, int company, DateOnly date)
    {
        ctx.Require(Permission.LockPeriod);
        ctx.RequireCompany(company);
        return hub.WriteAsync(company, tx =>
        {
            var current = LockRepository.LockedUpTo(tx.Connection!);
            var earlier = current is { } c && date < c;
            if (earlier) ctx.Require(Permission.UnlockPeriod);
            LockRepository.Set(tx, date);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), earlier ? "unlock" : "lock", "lock", "books", Json(current), Json(date)));
            return 0;
        }, _ => feed.Publish(new ChangeEvent(company, ChangeKind.Lock, null)));
    }

    public Task UnlockAsync(CallContext ctx, int company)
    {
        ctx.Require(Permission.UnlockPeriod);
        ctx.RequireCompany(company);
        return hub.WriteAsync(company, tx =>
        {
            var current = LockRepository.LockedUpTo(tx.Connection!);
            LockRepository.Set(tx, null);
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "unlock", "lock", "books", Json(current), null));
            return 0;
        }, _ => feed.Publish(new ChangeEvent(company, ChangeKind.Lock, null)));
    }

    /// <summary>Checks the audit chain of a company, or of host.db when company is null.</summary>
    public AuditVerification VerifyAudit(CallContext ctx, int? company)
    {
        using var c = OpenForAudit(ctx, company);
        return AuditLog.Verify(c);
    }

    public IReadOnlyList<StoredAuditEntry> Audit(CallContext ctx, int? company, AuditFilter filter)
    {
        using var c = OpenForAudit(ctx, company);
        return AuditLog.Read(c, filter);
    }

    public Task<BackupFile> BackupNowAsync(CallContext ctx, int? company)
    {
        ctx.Require(Permission.Backup);
        RequireTarget(ctx, company);
        return BackupAsync(company, ctx.Audit(time.GetUtcNow(), "backup", "database", Source(company), null, null), keep: null);
    }

    public IReadOnlyList<BackupFile> Backups(CallContext ctx, int? company)
    {
        ctx.Require(Permission.Backup);
        RequireTarget(ctx, company);
        return backups.List(data.BackupFolder, Source(company));
    }

    /// <summary>
    /// Replaces a company file with one of its listed backups. Everyone is logged out first, the current file is backed
    /// up, and a damaged backup changes nothing.
    /// </summary>
    public async Task RestoreAsync(CallContext ctx, int company, string backupPath)
    {
        ctx.Require(Permission.Restore);
        ctx.RequireCompany(company);
        var full = Path.GetFullPath(backupPath);
        if (!Backups(ctx, company).Any(b => string.Equals(Path.GetFullPath(b.Path), full, StringComparison.OrdinalIgnoreCase)))
            throw new ServiceException(ServiceError.Invalid, "Choose a backup from the list.");

        using (await hub.MaintenanceAsync(company))
        {
            // Both checks come before anyone is logged out, so a failure leaves everything as it was.
            try { BackupManager.CheckIntegrity(full); }
            catch (BackupFailedException) { throw new ServiceException(ServiceError.Invalid, "Backup file is damaged. Nothing was restored."); }
            BackupFile before;
            try
            {
                // Kept out of pruning: it holds any audit records written after the chosen backup.
                before = backups.Backup(data.CompanyPath(company), data.BackupFolder, "prerestore");
            }
            catch (Exception e) when (e is BackupFailedException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                throw new ServiceException(ServiceError.Invalid, "Could not copy the current file first. Nothing was restored.");
            }
            sessions.RemoveAll();
            feed.Publish(new ChangeEvent(company, ChangeKind.Restore, null));
            using var live = hub.OpenFile(company);
            try { backups.RestoreInto(full, live); }
            catch (BackupFailedException) { throw new ServiceException(ServiceError.Invalid, "Backup file is damaged. Nothing was restored."); }
            Migrator.Migrate(live, Schema.Company);
            books.Invalidate(company);
            using var tx = SqliteDatabase.BeginWrite(live);
            var after = JsonSerializer.Serialize(new { File = Path.GetFileName(full), PreRestoreCopy = Path.GetFileName(before.Path) });
            hub.Audit(tx, ctx.Audit(time.GetUtcNow(), "restore", "database", Source(company), null, after));
            tx.Commit();
            // Also in host.db, which a company restore never replaces.
            data.WriteHost(hostTx => AuditLog.Append(hostTx, ctx.Audit(time.GetUtcNow(), "restore", "company", Text.Key(company), null, after)));
        }
    }

    /// <summary>
    /// Backs up host.db (company null) or one company, prunes to <paramref name="keep"/> when given, and audits the
    /// backup in the database it copied.
    /// </summary>
    internal async Task<BackupFile> BackupAsync(int? company, AuditEntry audit, int? keep)
    {
        if (company is { } gated) hub.Gate.Check(gated);
        var path = company is { } id ? data.CompanyPath(id) : data.HostDbPath;
        if (!File.Exists(path)) throw new ServiceException(ServiceError.NotFound, "No such company.");
        var folder = data.BackupFolder;
        BackupFile file;
        try { file = backups.Backup(path, folder); }
        catch (BackupFailedException e) { throw new ServiceException(ServiceError.Invalid, e.Message); }
        if (keep is { } k) backups.Prune(folder, Source(company), k);

        var entry = audit with { After = JsonSerializer.Serialize(new { File = Path.GetFileName(file.Path) }) };
        if (company is { } companyId) await hub.WriteAsync(companyId, tx => { hub.Audit(tx, entry); return 0; }).ConfigureAwait(false);
        else data.WriteHost(tx => AuditLog.Append(tx, entry));
        return file;
    }

    internal IReadOnlyList<BackupFile> BackupsOf(int? company) => backups.List(data.BackupFolder, Source(company));

    Microsoft.Data.Sqlite.SqliteConnection OpenForAudit(CallContext ctx, int? company)
    {
        ctx.Require(Permission.ViewAudit);
        RequireTarget(ctx, company);
        return company is { } id ? hub.Open(id) : data.OpenHost();
    }

    // host.db holds every user and the list of all companies, so only a host administrator may read or copy it.
    static void RequireTarget(CallContext ctx, int? company)
    {
        if (company is { } id) ctx.RequireCompany(id);
        else ctx.Require(Permission.ManageHostSettings);
    }

    internal string Source(int? company) =>
        Path.GetFileNameWithoutExtension(company is { } id ? data.CompanyPath(id) : data.HostDbPath);

    static string? Json(DateOnly? d) => d is { } x ? JsonSerializer.Serialize(Text.Date(x)) : null;
}
