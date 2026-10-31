using System.Net;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class UsersScreenTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<UsersViewModel> User(ShellContext ctx)
    {
        Assert.True(CommandParser.TryParse("USER", out var cmd));
        var screen = (UsersViewModel)UsersViewModel.Create(cmd, ctx)!;
        await screen.LoadAsync(CancellationToken.None);
        return screen;
    }

    [Fact]
    public async Task Created_token_is_shown_once_and_gone_after_reload()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        u.NewTokenName = "scripts";
        await u.CreateTokenAsync();
        var token = u.CreatedToken;
        Assert.StartsWith("ohk_", token, StringComparison.Ordinal);
        Assert.Equal("Copy this token now. It is not shown again.", u.Message);
        Assert.Equal(("scripts", "Read only", "Never", ""), (u.Tokens[0].Name, u.Tokens[0].Access, u.Tokens[0].LastUsed, u.Tokens[0].Revoked));
        Assert.Equal("", u.NewTokenName);
        await u.LoadAsync(CancellationToken.None);
        Assert.Null(u.CreatedToken);
        Assert.DoesNotContain(u.Tokens, r => r.ToString().Contains(token!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Created_token_works_on_the_api()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        u.NewTokenName = "excel";
        u.NewTokenReadWrite = true;
        await u.CreateTokenAsync();
        var ctx = t.ApiTokens.Authenticate(u.CreatedToken, ChangeSource.Rest, IPAddress.Loopback);
        Assert.Equal(("owner", "excel", false), (ctx.UserName, ctx.TokenName, ctx.ReadOnly));
        Assert.Equal("Read and write", u.Tokens.Single().Access);
    }

    [Fact]
    public async Task Revoke_asks_and_no_keeps_the_token()
    {
        var confirm = new FakeConfirm(answer: false);
        var shell = await t.LoggedInShellAsync(open: [], confirm: confirm);
        var u = await User(shell.Context);
        u.NewTokenName = "scripts";
        await u.CreateTokenAsync();
        await u.RevokeAsync(u.Tokens[0]);
        Assert.Equal("Revoke scripts?", confirm.LastQuestion);
        Assert.Equal("", u.Tokens[0].Revoked);
    }

    [Fact]
    public async Task Revoke_with_yes_marks_the_token_revoked()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        u.NewTokenName = "scripts";
        await u.CreateTokenAsync();
        var token = u.CreatedToken;
        await u.RevokeAsync(u.Tokens[0]);
        Assert.NotEqual("", u.Tokens[0].Revoked);
        Assert.Throws<ServiceException>(() => t.ApiTokens.Authenticate(token, ChangeSource.Rest, IPAddress.Loopback));
    }

    [Fact]
    public async Task Duplicate_token_name_shows_the_host_message()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        u.NewTokenName = "scripts";
        await u.CreateTokenAsync();
        u.NewTokenName = "scripts";
        await u.CreateTokenAsync();
        Assert.Equal("You already have a token named scripts.", u.Message);
        Assert.Equal("scripts", u.NewTokenName);
        Assert.Null(u.CreatedToken);
    }

    [Fact]
    public async Task Accountant_sees_tokens_but_not_users()
    {
        var owner = await t.LoggedInShellAsync(open: [], company: "A Traders");
        var accountant = await t.UserShellAsync("asha", "Accountant", owner.Context.Company!.Id);
        var u = await User(accountant.Context);
        Assert.False(u.CanManageUsers);
        Assert.Empty(u.Users);
        u.NewTokenName = "mine";
        await u.CreateTokenAsync();
        Assert.Single(u.Tokens);
    }

    [Fact]
    public async Task Owner_creates_a_user_and_gives_one_company()
    {
        var shell = await t.LoggedInShellAsync(open: [], company: "A Traders");
        var b = await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        var u = await User(shell.Context);
        Assert.True(u.CanManageUsers);
        u.NewUserName = "meena";
        u.NewUserRole = "DataEntry";
        await u.CreateUserAsync("meena-pass-1");
        Assert.Equal("User created.", u.Message);
        Assert.Contains(u.Users, x => (x.Name, x.Role) == ("meena", "DataEntry"));
        u.SelectedUser = u.Users.Single(x => x.Name == "meena");
        Assert.All(u.CompanyChoices, c => Assert.False(c.Checked));
        u.CompanyChoices.Single(c => c.Name == "B Traders").Checked = true;
        await u.SaveCompaniesAsync();
        await u.LoadAsync(CancellationToken.None);
        u.SelectedUser = u.Users.Single(x => x.Name == "meena");
        Assert.Equal([b], u.CompanyChoices.Where(c => c.Checked).Select(c => c.Id));
    }

    [Fact]
    public async Task Differing_new_passwords_call_nothing()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        var before = t.Requests.Count;
        await u.ChangePasswordAsync(ClientTestHost.Password, "new-password-1", "new-password-2");
        Assert.Equal("The new passwords differ.", u.Message);
        Assert.Equal(before, t.Requests.Count);
    }

    [Fact]
    public async Task Password_change_works_and_a_wrong_current_one_shows_the_host_message()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var u = await User(shell.Context);
        await u.ChangePasswordAsync("wrong-password", "new-password-1", "new-password-1");
        Assert.False(string.IsNullOrEmpty(u.Message));
        Assert.NotEqual("Password changed.", u.Message);
        await u.ChangePasswordAsync(ClientTestHost.Password, "new-password-1", "new-password-1");
        Assert.Equal("Password changed.", u.Message);
    }
}
