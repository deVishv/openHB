namespace OpenHisabKitab.Host.Tests;

public sealed class AdminServiceTests : IDisposable
{
    static readonly DateOnly May10 = new(2026, 5, 10);
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId cash, sales;

    public AdminServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
    }

    public void Dispose() => host.Dispose();

    JournalInput Receipt(long rupees) =>
        new(VoucherType.Receipt, May10, "", [new PostingLine(cash, Money.FromRupees(rupees)), new PostingLine(sales, Money.FromRupees(-rupees))]);

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    static async Task<ServiceException> FailsAsync(ServiceError error, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    [Fact]
    public async Task Accountant_can_lock_but_not_move_the_lock_earlier_and_owner_can_unlock()
    {
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        await host.Admin.LockUpToAsync(meena, company, new DateOnly(2026, 6, 30));
        await host.Admin.LockUpToAsync(meena, company, new DateOnly(2026, 7, 31));
        await FailsAsync(ServiceError.PermissionDenied, () => host.Admin.LockUpToAsync(meena, company, new DateOnly(2026, 6, 30)));
        await FailsAsync(ServiceError.PermissionDenied, () => host.Admin.UnlockAsync(meena, company));
        await host.Admin.LockUpToAsync(owner, company, new DateOnly(2026, 6, 30));
        await host.Admin.UnlockAsync(owner, company);
        using var c = host.Hub.Open(company);
        Assert.Null(LockRepository.LockedUpTo(c));
        var actions = AuditLog.Read(c, new AuditFilter(Entity: "lock")).Select(r => r.Entry.Action).Reverse();
        Assert.Equal(["lock", "lock", "unlock", "unlock"], actions);
    }

    [Fact]
    public async Task Viewer_cannot_lock()
    {
        var ravi = host.NewUser("ravi", BuiltInRole.Viewer, company).Context;
        await FailsAsync(ServiceError.PermissionDenied, () => host.Admin.LockUpToAsync(ravi, company, new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public async Task Audit_verifies_after_mixed_work_and_reports_tampering()
    {
        for (var i = 1; i <= 8; i++) await host.Vouchers.SaveJournalAsync(owner, company, Receipt(i));
        await host.Masters.AddLedgerAsync(owner, company, "HDFC Bank", host.Masters.Chart(owner, company).Group(GroupKind.BankAccounts));
        await host.Admin.LockUpToAsync(owner, company, new DateOnly(2026, 4, 30));
        await host.Admin.BackupNowAsync(owner, company);
        var ok = host.Admin.VerifyAudit(owner, company);
        Assert.True(ok.Ok);
        Assert.True(ok.Count >= 12);
        Assert.True(host.Admin.VerifyAudit(owner, null).Ok);
        Assert.NotEmpty(host.Admin.Audit(owner, company, new AuditFilter(Entity: "voucher")));

        using (var c = host.Hub.Open(company))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DROP TRIGGER audit_log_no_update; UPDATE audit_log SET user = 'someone' WHERE seq = 5;";
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(5, host.Admin.VerifyAudit(owner, company).FirstBadSeq);
    }

    [Fact]
    public void Data_entry_cannot_view_the_audit()
    {
        var asha = host.NewUser("asha", BuiltInRole.DataEntry, company).Context;
        Fails(ServiceError.PermissionDenied, () => host.Admin.VerifyAudit(asha, company));
    }

    [Fact]
    public async Task Backup_now_is_listed_and_audited()
    {
        var b = await host.Admin.BackupNowAsync(owner, company);
        Assert.Equal(b.Path, host.Admin.Backups(owner, company).Single().Path);
        var hostBackup = await host.Admin.BackupNowAsync(owner, null);
        Assert.Equal("host", hostBackup.SourceName);
        using var c = host.Hub.Open(company);
        Assert.Contains(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "backup" && r.Entry.After!.Contains(Path.GetFileName(b.Path)));
    }

    [Fact]
    public async Task Restore_brings_back_the_backup_and_ends_all_sessions()
    {
        var a = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = await host.Admin.BackupNowAsync(owner, company);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var b = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(200));
        var cashBefore = host.Reports.TrialBalance(owner, company, new FinancialYear(2026).Range).Rows.Single(r => r.Ledger == cash).Debits;
        Assert.Equal(Money.FromRupees(300), cashBefore);
        var other = host.Auth.Login("owner", TestHost.OwnerPassword, "PC2");

        await host.Admin.RestoreAsync(owner, company, backup.Path);

        Assert.Null(host.Sessions.Resolve(other.Token));
        Assert.Null(host.Sessions.Resolve(host.Owner.Token));
        Assert.Equal(a.Id, host.Vouchers.Get(owner, company, a.Id).Id);
        Assert.Equal(ServiceError.NotFound, Assert.Throws<ServiceException>(() => host.Vouchers.Get(owner, company, b.Id)).Error);
        var cashAfter = host.Reports.TrialBalance(owner, company, new FinancialYear(2026).Range).Rows.Single(r => r.Ledger == cash).Debits;
        Assert.Equal(Money.FromRupees(100), cashAfter);
        Assert.True(host.Admin.VerifyAudit(owner, company).Ok);
        Assert.Contains(host.Admin.Audit(owner, company, new AuditFilter()), r => r.Entry.Action == "restore");
        Assert.Single(host.Admin.Backups(owner, company));
        using var hostDb = host.Data.OpenHost();
        var restore = AuditLog.Read(hostDb, new AuditFilter()).Single(r => r.Entry.Action == "restore");
        Assert.Contains(Path.GetFileName(backup.Path), restore.Entry.After);
        Assert.Contains("prerestore", restore.Entry.After);
    }

    [Fact]
    public async Task Damaged_backup_restores_nothing()
    {
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = await host.Admin.BackupNowAsync(owner, company);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var b = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(200));
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(backup.Path);
        File.WriteAllBytes(backup.Path, bytes[..(bytes.Length / 2)]);

        var ex = await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(owner, company, backup.Path));
        Assert.Equal(ServiceError.Invalid, ex.Error);
        Assert.Equal("Backup file is damaged. Nothing was restored.", ex.Message);
        Assert.Equal(b.Id, host.Vouchers.Get(owner, company, b.Id).Id);
        Assert.False(host.Hub.Gate.IsClosed(company));
        Assert.NotNull(host.Sessions.Resolve(host.Owner.Token));
    }

    [Fact]
    public async Task Only_listed_backups_can_be_restored_and_only_by_the_owner()
    {
        var backup = await host.Admin.BackupNowAsync(owner, company);
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        Assert.Equal(ServiceError.PermissionDenied,
            (await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(meena, company, backup.Path))).Error);
        var stray = Path.Combine(host.Folder, "elsewhere.db");
        File.Copy(backup.Path, stray);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(owner, company, stray));
        Assert.Equal("Choose a backup from the list.", ex.Message);
    }

    [Fact]
    public async Task Closed_gate_refuses_company_work()
    {
        var other = host.NewCompany(TestHost.Profile("B Traders"));
        host.Hub.Gate.Close(company);
        var ex = Fails(ServiceError.Unavailable, () => host.Masters.Chart(owner, company));
        Assert.Equal("A restore is running. Try again in a minute.", ex.Message);
        await FailsAsync(ServiceError.Unavailable, () => host.Admin.BackupNowAsync(owner, company));
        Assert.NotEmpty(host.Masters.Chart(owner, other).Ledgers);
        await host.Admin.BackupNowAsync(owner, other);
        await host.Scheduler.RunOnceAsync();
        using (var c = host.Data.OpenHost())
            Assert.DoesNotContain(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "backup-failed" && r.Entry.EntityKey == "company-" + other);
        host.Hub.Gate.Open(company);
        Assert.NotEmpty(host.Masters.Chart(owner, company).Ledgers);
    }

    [Fact]
    public async Task Host_database_audit_and_backups_are_for_the_owner_only()
    {
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        Fails(ServiceError.PermissionDenied, () => host.Admin.VerifyAudit(meena, null));
        Fails(ServiceError.PermissionDenied, () => host.Admin.Audit(meena, null, new AuditFilter()));
        await FailsAsync(ServiceError.PermissionDenied, () => host.Admin.BackupNowAsync(meena, null));
        Fails(ServiceError.PermissionDenied, () => host.Admin.Backups(meena, null));
        Assert.True(host.Admin.VerifyAudit(meena, company).Ok);
        await host.Admin.BackupNowAsync(meena, company);
    }

    [Fact]
    public async Task Copy_taken_before_a_restore_is_never_pruned()
    {
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = await host.Admin.BackupNowAsync(owner, company);
        await host.Admin.RestoreAsync(owner, company, backup.Path);
        var kept = Directory.GetFiles(host.Data.BackupFolder, "*prerestore*.db");
        Assert.Single(kept);
        host.Data.WriteHost(tx => { HostSettingsRepository.Set(tx, "backup.keep", "1"); return 0; });
        for (var i = 0; i < 3; i++) { host.Time.Advance(TimeSpan.FromDays(1)); await host.Scheduler.RunOnceAsync(); }
        Assert.True(File.Exists(kept[0]));
        Assert.Single(host.Admin.Backups(owner, company));
    }

    [Fact]
    public async Task Failed_copy_before_restore_restores_nothing()
    {
        var a = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = await host.Admin.BackupNowAsync(owner, company);
        var b = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(200));
        var folder = new DirectoryInfo(host.Data.BackupFolder);
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(me,
            System.Security.AccessControl.FileSystemRights.WriteData | System.Security.AccessControl.FileSystemRights.CreateFiles,
            System.Security.AccessControl.AccessControlType.Deny);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        try
        {
            var ex = await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(owner, company, backup.Path));
            Assert.Equal(ServiceError.Invalid, ex.Error);
            Assert.Equal("Could not copy the current file first. Nothing was restored.", ex.Message);
        }
        finally
        {
            acl = folder.GetAccessControl();
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }
        Assert.Equal(b.Id, host.Vouchers.Get(owner, company, b.Id).Id);
        Assert.Equal(a.Id, host.Vouchers.Get(owner, company, a.Id).Id);
        Assert.NotNull(host.Sessions.Resolve(host.Owner.Token));
    }

    [Fact]
    public async Task Removing_the_newest_audit_rows_of_a_company_is_reported()
    {
        for (var i = 1; i <= 3; i++) await host.Vouchers.SaveJournalAsync(owner, company, Receipt(i));
        Assert.Equal(AnchorCheck.Matches, host.Admin.VerifyAudit(owner, company).Anchor);

        using (var c = host.Hub.Open(company))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DROP TRIGGER audit_log_no_delete; DELETE FROM audit_log WHERE seq = (SELECT max(seq) FROM audit_log);";
            cmd.ExecuteNonQuery();
        }
        var v = host.Admin.VerifyAudit(owner, company);
        Assert.Null(v.FirstBadSeq);
        Assert.False(v.Ok);
        Assert.Equal(AnchorCheck.Mismatch, v.Anchor);
    }

    [Fact]
    public void A_new_company_is_anchored_from_its_first_record()
    {
        var v = host.Admin.VerifyAudit(owner, company);
        Assert.True(v.Ok);
        Assert.Equal(AnchorCheck.Matches, v.Anchor);
    }
}
