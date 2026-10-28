namespace OpenHisabKitab.Host.Tests;

public sealed class BackupSchedulerTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;

    public BackupSchedulerTests()
    {
        company = host.NewCompany();
    }

    public void Dispose() => host.Dispose();

    int CompanyBackups() => host.Admin.Backups(host.Owner.Context, company).Count;

    [Fact]
    public async Task Backs_up_when_the_newest_is_a_day_old()
    {
        await host.Scheduler.RunOnceAsync();
        Assert.Equal(1, CompanyBackups());
        Assert.Single(host.Admin.Backups(host.Owner.Context, null));
        host.Time.Advance(TimeSpan.FromHours(23));
        await host.Scheduler.RunOnceAsync();
        Assert.Equal(1, CompanyBackups());
        host.Time.Advance(TimeSpan.FromHours(1));
        await host.Scheduler.RunOnceAsync();
        Assert.Equal(2, CompanyBackups());
    }

    [Fact]
    public async Task Keeps_the_configured_number()
    {
        host.Data.WriteHost(tx => { HostSettingsRepository.Set(tx, "backup.keep", "3"); return 0; });
        for (var i = 0; i < 5; i++)
        {
            await host.Scheduler.RunOnceAsync();
            host.Time.Advance(TimeSpan.FromHours(24));
        }
        Assert.Equal(3, CompanyBackups());
    }

    [Fact]
    public async Task Scheduled_backup_is_audited_as_the_system()
    {
        await host.Scheduler.RunOnceAsync();
        using var c = host.Hub.Open(company);
        var entry = AuditLog.Read(c, new AuditFilter()).First(r => r.Entry.Action == "backup").Entry;
        Assert.Equal(ChangeSource.System, entry.Source);
        Assert.Equal("system", entry.User);
    }

    [Fact]
    public async Task A_failure_is_audited_and_other_companies_still_back_up()
    {
        var second = host.NewCompany(TestHost.Profile("B Traders"));
        host.Data.WriteHost(tx => { HostSettingsRepository.Set(tx, "backup.folder", System.Text.Json.JsonSerializer.Serialize(Path.Combine(host.Folder, "Backups"))); return 0; });
        SqliteConnection.ClearAllPools();
        File.Move(host.Data.CompanyPath(company), host.Data.CompanyPath(company) + ".gone");
        await host.Scheduler.RunOnceAsync();
        Assert.Single(host.Admin.Backups(host.Owner.Context, second));
        using var c = host.Data.OpenHost();
        Assert.Contains(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "backup-failed");
    }
}
