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

    [Fact]
    public void Accountant_can_lock_but_not_move_the_lock_earlier_and_owner_can_unlock()
    {
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        host.Admin.LockUpTo(meena, company, new DateOnly(2026, 6, 30));
        host.Admin.LockUpTo(meena, company, new DateOnly(2026, 7, 31));
        Fails(ServiceError.PermissionDenied, () => host.Admin.LockUpTo(meena, company, new DateOnly(2026, 6, 30)));
        Fails(ServiceError.PermissionDenied, () => host.Admin.Unlock(meena, company));
        host.Admin.LockUpTo(owner, company, new DateOnly(2026, 6, 30));
        host.Admin.Unlock(owner, company);
        using var c = host.Hub.Open(company);
        Assert.Null(LockRepository.LockedUpTo(c));
        var actions = AuditLog.Read(c, new AuditFilter(Entity: "lock")).Select(r => r.Entry.Action).Reverse();
        Assert.Equal(["lock", "lock", "unlock", "unlock"], actions);
    }

    [Fact]
    public void Viewer_cannot_lock()
    {
        var ravi = host.NewUser("ravi", BuiltInRole.Viewer, company).Context;
        Fails(ServiceError.PermissionDenied, () => host.Admin.LockUpTo(ravi, company, new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public async Task Audit_verifies_after_mixed_work_and_reports_tampering()
    {
        for (var i = 1; i <= 8; i++) await host.Vouchers.SaveJournalAsync(owner, company, Receipt(i));
        host.Masters.AddLedger(owner, company, "HDFC Bank", host.Masters.Chart(owner, company).Group(GroupKind.BankAccounts));
        host.Admin.LockUpTo(owner, company, new DateOnly(2026, 4, 30));
        host.Admin.BackupNow(owner, company);
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
    public void Backup_now_is_listed_and_audited()
    {
        var b = host.Admin.BackupNow(owner, company);
        Assert.Equal(b.Path, host.Admin.Backups(owner, company).Single().Path);
        var hostBackup = host.Admin.BackupNow(owner, null);
        Assert.Equal("host", hostBackup.SourceName);
        using var c = host.Hub.Open(company);
        Assert.Contains(AuditLog.Read(c, new AuditFilter()), r => r.Entry.Action == "backup" && r.Entry.After!.Contains(Path.GetFileName(b.Path)));
    }

    [Fact]
    public async Task Restore_brings_back_the_backup_and_ends_all_sessions()
    {
        var a = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = host.Admin.BackupNow(owner, company);
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
        Assert.Equal(2, host.Admin.Backups(owner, company).Count);
    }

    [Fact]
    public async Task Damaged_backup_restores_nothing()
    {
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt(100));
        var backup = host.Admin.BackupNow(owner, company);
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var b = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(200));
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(backup.Path);
        File.WriteAllBytes(backup.Path, bytes[..(bytes.Length / 2)]);

        var ex = await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(owner, company, backup.Path));
        Assert.Equal(ServiceError.Invalid, ex.Error);
        Assert.Equal("Backup file is damaged. Nothing was restored.", ex.Message);
        Assert.Equal(b.Id, host.Vouchers.Get(owner, company, b.Id).Id);
        Assert.False(host.Hub.Gate.IsClosed);
    }

    [Fact]
    public async Task Only_listed_backups_can_be_restored_and_only_by_the_owner()
    {
        var backup = host.Admin.BackupNow(owner, company);
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        Assert.Equal(ServiceError.PermissionDenied,
            (await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(meena, company, backup.Path))).Error);
        var stray = Path.Combine(host.Folder, "elsewhere.db");
        File.Copy(backup.Path, stray);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => host.Admin.RestoreAsync(owner, company, stray));
        Assert.Equal("Choose a backup from the list.", ex.Message);
    }

    [Fact]
    public void Closed_gate_refuses_company_work()
    {
        host.Hub.Gate.Close();
        var ex = Fails(ServiceError.Unavailable, () => host.Masters.Chart(owner, company));
        Assert.Equal("A restore is running. Try again in a minute.", ex.Message);
        host.Hub.Gate.Open();
        Assert.NotEmpty(host.Masters.Chart(owner, company).Ledgers);
    }

    [Fact]
    public void Host_database_audit_and_backups_are_for_the_owner_only()
    {
        var meena = host.NewUser("meena", BuiltInRole.Accountant, company).Context;
        Fails(ServiceError.PermissionDenied, () => host.Admin.VerifyAudit(meena, null));
        Fails(ServiceError.PermissionDenied, () => host.Admin.Audit(meena, null, new AuditFilter()));
        Fails(ServiceError.PermissionDenied, () => host.Admin.BackupNow(meena, null));
        Fails(ServiceError.PermissionDenied, () => host.Admin.Backups(meena, null));
        Assert.True(host.Admin.VerifyAudit(meena, company).Ok);
        host.Admin.BackupNow(meena, company);
    }
}
