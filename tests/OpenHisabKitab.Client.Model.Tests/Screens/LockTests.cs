using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class LockTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<LockViewModel> Lock(ShellContext ctx, string input = "LOCK")
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var s = (LockViewModel)LockViewModel.Create(cmd, ctx)!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    async Task<string> Stored(ShellContext ctx) =>
        (await t.OtherOwnerClient().Admin.GetLockAsync(new CompanyRef { Company = ctx.Company!.Id }, t.Headers())).LockedUpTo;

    [Fact]
    public async Task Lock_shows_the_current_lock_and_locks_a_date()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Lock(shell.Context, "LOCK 30-06-2026");
        Assert.Equal("Not locked", s.Current);
        Assert.Equal("30-06-2026", s.NewDate);
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.Equal("Locked up to 30-06-2026", s.Current);
        Assert.Equal("2026-06-30", await Stored(shell.Context));
    }

    [Fact]
    public async Task Moving_the_lock_earlier_asks_first()
    {
        var confirm = new FakeConfirm(false);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        await t.OtherOwnerClient().Admin.LockUpToAsync(new LockRequest { Company = shell.Context.Company!.Id, Date = "2026-06-30" }, t.Headers());
        var s = await Lock(shell.Context);
        s.NewDate = "31-05-2026";
        await s.SaveAsync();
        Assert.Equal("Unlock vouchers after 31-05-2026?", confirm.LastQuestion);
        Assert.Equal("2026-06-30", await Stored(shell.Context));
    }

    [Fact]
    public async Task Unlock_asks_then_unlocks()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        await t.OtherOwnerClient().Admin.LockUpToAsync(new LockRequest { Company = shell.Context.Company!.Id, Date = "2026-06-30" }, t.Headers());
        var s = await Lock(shell.Context);
        Assert.True(s.CanUnlock);
        await s.UnlockAsync();
        Assert.Equal("Unlock all periods?", confirm.LastQuestion);
        Assert.Equal("Not locked", s.Current);
        Assert.Equal("", await Stored(shell.Context));
    }

    [Fact]
    public async Task Accountant_cannot_unlock()
    {
        var owner = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(owner);
        var shell = await t.UserShellAsync("acc", "Accountant", company);
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = company, Name = "A Traders" });
        var s = await Lock(shell.Context);
        Assert.True(s.CanLock);
        Assert.False(s.CanUnlock);
    }

    [Fact]
    public async Task A_bad_date_says_how_to_write_it()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Lock(shell.Context);
        s.NewDate = "31-02-2026";
        await s.SaveAsync();
        Assert.Equal("Enter the date as dd-mm-yyyy.", s.Message);
    }
}
