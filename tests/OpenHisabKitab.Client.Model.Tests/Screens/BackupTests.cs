using Grpc.Core;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class BackupTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<BackupViewModel> Open(ShellViewModel shell)
    {
        Assert.True(Core.Commands.CommandParser.TryParse("BKUP", out var cmd));
        var s = (BackupViewModel)BackupViewModel.Create(cmd, shell.Context)!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    [Fact]
    public async Task Backup_now_adds_a_row()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());
        await shell.RunAsync("BKUP", newTab: true);
        var s = Assert.IsType<BackupViewModel>(shell.Tabs.Active!.Screen);
        var before = s.Rows.Count;
        await s.BackupNowAsync();
        Assert.Equal(before + 1, s.Rows.Count);
        Assert.StartsWith("Backed up to company-", s.Message, StringComparison.Ordinal);
        Assert.EndsWith(".db.", s.Message, StringComparison.Ordinal);
        Assert.True((s.CanBackup, s.CanRestore, s.CanSchedule) == (true, true, true));
        Assert.Equal(("24", "30", ""), (s.IntervalHours, s.Keep, s.Folder));
    }

    [Fact]
    public async Task Accountant_backs_up_but_cannot_restore()
    {
        var owner = await t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());
        var accountant = await t.UserShellAsync("acct", "Accountant", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(accountant.Context, new CompanyEntry { Id = owner.Context.Company!.Id, Name = "A Traders" });
        var s = await Open(accountant);
        Assert.Null(s.Message);
        Assert.Equal((true, false, false), (s.CanBackup, s.CanRestore, s.CanSchedule));
        await s.BackupNowAsync();
        Assert.NotEmpty(s.Rows);
    }

    [Fact]
    public async Task Owner_restore_asks_then_logs_everyone_out()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built(), confirm: confirm);
        var s = await Open(shell);
        await s.BackupNowAsync();
        s.Selected = s.Rows[0];
        var other = t.Headers();   // a second session, made before the restore
        await s.RestoreAsync();
        Assert.Equal($"Restore {s.Rows[0].File}? Everyone is logged out and the current file is backed up first.", confirm.LastQuestion);
        Assert.Null(s.Message);
        var e = await Assert.ThrowsAsync<RpcException>(async () =>
            await t.OtherOwnerClient().Companies.ListAsync(new Empty(), other));
        Assert.Equal(StatusCode.Unauthenticated, e.StatusCode);
    }

    [Fact]
    public async Task Restore_declined_does_nothing()
    {
        var confirm = new FakeConfirm(false);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built(), confirm: confirm);
        var s = await Open(shell);
        await s.BackupNowAsync();
        s.Selected = s.Rows[0];
        await s.RestoreAsync();
        Assert.Equal(1, confirm.Asked);
        Assert.NotEmpty((await t.OtherOwnerClient().Companies.ListAsync(new Empty(), t.Headers())).Companies);
    }

    [Fact]
    public async Task Schedule_saves_changed_settings_only()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());
        var s = await Open(shell);
        s.Keep = "7";
        await s.SaveAsync();
        Assert.Equal("Backup schedule saved.", s.Message);
        var audit = await t.OtherOwnerClient().Admin.ListAuditAsync(new ListAuditRequest { Company = 0, Entity = "host-setting" }, t.Headers());
        Assert.Equal(["backup.keep"], audit.Entries.Select(x => x.EntityKey));

        s.Keep = "0";
        await s.SaveAsync();
        Assert.Equal("backup.keep must be 1 to 365.", s.Message);
        s.Keep = "seven";
        await s.SaveAsync();
        Assert.Equal("backup.keep must be 1 to 365.", s.Message);
    }
}
