using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Scheduled backups (spec 9). Every minute it backs up host.db and each company whose newest backup is at least
/// backup.intervalHours old (default 24), keeping backup.keep copies (default 30). A PC that was off at night backs up
/// soon after it starts.
/// </summary>
public sealed class BackupScheduler(AdminService admin, HostData data, TimeProvider time, ILogger<BackupScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { RunOnce(); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Scheduled backup check failed."); }
            await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken).ConfigureAwait(false);
        }
    }

    public void RunOnce()
    {
        int interval, keep;
        IReadOnlyList<int> companies;
        using (var c = data.OpenHost())
        {
            interval = Setting(c, "backup.intervalHours", 24);
            keep = Setting(c, "backup.keep", 30);
            companies = [.. CompanyListRepository.List(c).Select(e => e.Id)];
        }

        foreach (var company in companies.Select(id => (int?)id).Prepend(null))
        {
            var newest = admin.BackupsOf(company).FirstOrDefault();
            if (newest is not null && time.GetUtcNow() - newest.TakenAt < TimeSpan.FromHours(interval)) continue;
            var source = admin.Source(company);
            try
            {
                admin.Backup(company, SystemEntry("backup", source, null), keep);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Backup of {Source} failed.", source);
                data.WriteHost(tx => AuditLog.Append(tx, SystemEntry("backup-failed", source, JsonSerializer.Serialize(new { e.Message }))));
            }
        }
    }

    AuditEntry SystemEntry(string action, string source, string? after) =>
        new(time.GetUtcNow(), "system", Environment.MachineName, ChangeSource.System, null, action, "database", source, null, after);

    static int Setting(Microsoft.Data.Sqlite.SqliteConnection c, string key, int fallback) =>
        HostSettingsRepository.Get(c, key) is { } json && JsonSerializer.Deserialize<int>(json) is > 0 and var v ? v : fallback;
}
