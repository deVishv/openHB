using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace OpenHisabKitab.Host.Tests;

public sealed class ApiTokenServiceTests : IDisposable
{
    readonly TestHost h = new();

    public void Dispose() => h.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    List<string> HostDbColumn(string sql)
    {
        using var c = h.Data.OpenHost();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        return list;
    }

    void HostDbExecute(string sql)
    {
        using var c = h.Data.OpenHost();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    CallContext Auth(string token, ChangeSource source = ChangeSource.Rest, IPAddress? from = null) =>
        h.Tokens.Authenticate(token, source, from ?? IPAddress.Loopback);

    [Fact]
    public void Token_is_shown_once_and_only_its_hash_is_stored()
    {
        var t = h.Tokens.Create(h.Owner.Context, "scripts", readWrite: false);
        Assert.Matches("^ohk_[A-Za-z0-9_-]{43}$", t.Token);
        Assert.Equal(("scripts", false), (t.Name, t.ReadWrite));
        var stored = Assert.Single(HostDbColumn("SELECT token_hash FROM api_tokens"));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(t.Token))), stored);
        Assert.DoesNotContain(HostDbColumn("SELECT coalesce(before,'') || coalesce(after,'') FROM audit_log"), s => s.Contains(t.Token, StringComparison.Ordinal));
        Assert.DoesNotContain(t.Token, string.Join("|", h.Tokens.List(h.Owner.Context)), StringComparison.Ordinal);
    }

    [Fact]
    public void Authenticated_context_carries_source_token_name_and_user_limits()
    {
        var a = h.NewCompany();
        var b = h.NewCompany(TestHost.Profile("B Traders"));
        var clerk = h.NewUser("clerk", BuiltInRole.DataEntry, a);
        var t = h.Tokens.Create(clerk.Context, "excel", readWrite: true);
        var ctx = Auth(t.Token);
        Assert.Equal((ChangeSource.Rest, "excel", "clerk", false), (ctx.Source, ctx.TokenName, ctx.UserName, ctx.ReadOnly));
        Assert.Equal(Environment.MachineName, ctx.Machine);
        ctx.RequireCompany(a);
        Assert.Throws<ServiceException>(() => ctx.RequireCompany(b));
        Assert.False(ctx.Has(Permission.ViewReports));
        ctx.Require(Permission.CreateVoucher);
    }

    [Fact]
    public void Lan_caller_is_named_by_address()
    {
        var t = h.Tokens.Create(h.Owner.Context, "lan", readWrite: false);
        Assert.Equal("192.168.1.9", Auth(t.Token, from: IPAddress.Parse("192.168.1.9")).Machine);
    }

    [Fact]
    public void Read_only_token_refuses_writes_with_its_reason()
    {
        var t = h.Tokens.Create(h.Owner.Context, "ro", readWrite: false);
        var ctx = Auth(t.Token);
        var e = Fails(ServiceError.PermissionDenied, () => ctx.Require(Permission.CreateVoucher));
        Assert.Equal("This token is read only. Create a read and write token to make changes.", e.Message);
        ctx.Require(Permission.ViewReports);
    }

    [Fact]
    public void Missing_role_permission_keeps_the_plain_refusal()
    {
        var co = h.NewCompany();
        var viewer = h.NewUser("viewer", BuiltInRole.Viewer, co);
        var t = h.Tokens.Create(viewer.Context, "ro", readWrite: false);
        Assert.Equal("You do not have permission for this.", Fails(ServiceError.PermissionDenied, () => Auth(t.Token).Require(Permission.CreateVoucher)).Message);
    }

    [Fact]
    public void Mcp_read_write_token_is_read_only_until_writes_are_allowed()
    {
        var t = h.Tokens.Create(h.Owner.Context, "claude", readWrite: true);
        var e = Fails(ServiceError.PermissionDenied, () => Auth(t.Token, ChangeSource.Mcp).Require(Permission.CreateVoucher));
        Assert.Equal("Changes through MCP are off. The Owner can turn on mcp.allowWrites.", e.Message);
        Auth(t.Token, ChangeSource.Rest).Require(Permission.CreateVoucher);
        h.HostSettings.Set(h.Owner.Context, "mcp.allowWrites", "true");
        Auth(t.Token, ChangeSource.Mcp).Require(Permission.CreateVoucher);
    }

    [Fact]
    public void Revoked_token_fails_on_the_next_request()
    {
        var t = h.Tokens.Create(h.Owner.Context, "x", readWrite: false);
        Auth(t.Token);
        h.Tokens.Revoke(h.Owner.Context, t.Id);
        Assert.Equal("Send a valid API token.", Fails(ServiceError.NotAuthenticated, () => Auth(t.Token)).Message);
    }

    [Fact]
    public void Inactive_user_token_fails()
    {
        h.NewUser("clerk", BuiltInRole.DataEntry);
        var clerk = h.Auth.Login("clerk", "password-clerk", "PC2");
        var t = h.Tokens.Create(clerk.Context, "x", readWrite: false);
        HostDbExecute("UPDATE users SET active = 0 WHERE name = 'clerk'");
        Assert.Equal("Send a valid API token.", Fails(ServiceError.NotAuthenticated, () => Auth(t.Token)).Message);
    }

    [Fact]
    public void Missing_bearer_is_refused_without_counting()
    {
        for (var i = 0; i < 10; i++)
            Assert.Equal("Send a valid API token.", Fails(ServiceError.NotAuthenticated, () => h.Tokens.Authenticate(null, ChangeSource.Rest, IPAddress.Loopback)).Message);
        Fails(ServiceError.NotAuthenticated, () => Auth("ohk_bad"));
    }

    [Fact]
    public void Token_cannot_mint_or_revoke_tokens()
    {
        var t = h.Tokens.Create(h.Owner.Context, "rw", readWrite: true);
        foreach (var source in new[] { ChangeSource.Rest, ChangeSource.Mcp })
        {
            var ctx = Auth(t.Token, source);
            Assert.Equal("Create and revoke tokens in Open Hisab Kitab.",
                Fails(ServiceError.PermissionDenied, () => h.Tokens.Create(ctx, "more", true)).Message);
            Assert.Equal("Create and revoke tokens in Open Hisab Kitab.",
                Fails(ServiceError.PermissionDenied, () => h.Tokens.Revoke(ctx, t.Id)).Message);
        }
    }

    [Fact]
    public void Users_see_and_revoke_only_their_own_tokens()
    {
        var mine = h.Tokens.Create(h.Owner.Context, "owner-token", readWrite: false);
        h.NewUser("clerk", BuiltInRole.DataEntry);
        var clerk = h.Auth.Login("clerk", "password-clerk", "PC2");
        var theirs = h.Tokens.Create(clerk.Context, "clerk-token", readWrite: false);
        Assert.Equal(["clerk-token"], h.Tokens.List(clerk.Context).Select(t => t.Name));
        Assert.Equal(["owner-token"], h.Tokens.List(h.Owner.Context).Select(t => t.Name));
        Assert.Equal("No such token.", Fails(ServiceError.NotFound, () => h.Tokens.Revoke(clerk.Context, mine.Id)).Message);
        Assert.Equal("No such token.", Fails(ServiceError.NotFound, () => h.Tokens.Revoke(h.Owner.Context, theirs.Id)).Message);
        Auth(mine.Token);
    }

    [Fact]
    public void Duplicate_and_bad_names_are_refused()
    {
        const string bad = "Enter a token name of up to 60 characters.";
        Assert.Equal(bad, Fails(ServiceError.Invalid, () => h.Tokens.Create(h.Owner.Context, "  ", false)).Message);
        Assert.Equal(bad, Fails(ServiceError.Invalid, () => h.Tokens.Create(h.Owner.Context, new string('a', 61), false)).Message);
        Assert.Equal(bad, Fails(ServiceError.Invalid, () => h.Tokens.Create(h.Owner.Context, "a\nb", false)).Message);
        h.Tokens.Create(h.Owner.Context, new string('a', 60), false);
        var x = h.Tokens.Create(h.Owner.Context, "x", false);
        Assert.Equal("You already have a token named x.", Fails(ServiceError.Invalid, () => h.Tokens.Create(h.Owner.Context, "x", true)).Message);
        h.Tokens.Revoke(h.Owner.Context, x.Id);
        h.Tokens.Create(h.Owner.Context, "x", true);
    }

    [Fact]
    public void Five_bad_tokens_from_one_address_slow_that_address_only()
    {
        var other = IPAddress.Parse("192.168.1.9");
        for (var i = 0; i < 5; i++) Fails(ServiceError.NotAuthenticated, () => Auth("ohk_bad"));
        var e = Fails(ServiceError.TooManyAttempts, () => Auth("ohk_bad"));
        Assert.Equal("Too many attempts. Try again in 30 seconds.", e.Message);
        Assert.Equal(TimeSpan.FromSeconds(30), e.RetryAfter);
        Fails(ServiceError.NotAuthenticated, () => Auth("ohk_bad", from: other));
        var good = h.Tokens.Create(h.Owner.Context, "good", readWrite: false);
        Auth(good.Token);   // a valid token is never slowed
    }

    [Fact]
    public void Last_used_is_written_at_most_once_a_minute()
    {
        var t = h.Tokens.Create(h.Owner.Context, "x", readWrite: false);
        var first = h.Time.GetUtcNow();
        Auth(t.Token);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        Auth(t.Token);
        Assert.Equal(first, Assert.Single(h.Tokens.List(h.Owner.Context)).LastUsedAt);
        h.Time.Advance(TimeSpan.FromSeconds(61));
        Auth(t.Token);
        Assert.Equal(h.Time.GetUtcNow(), Assert.Single(h.Tokens.List(h.Owner.Context)).LastUsedAt);
    }

    [Fact]
    public void Create_and_revoke_are_audited_without_the_token_text()
    {
        var t = h.Tokens.Create(h.Owner.Context, "x", readWrite: true);
        h.Tokens.Revoke(h.Owner.Context, t.Id);
        var rows = h.Admin.Audit(h.Owner.Context, null, new AuditFilter(Entity: "api-token")).OrderBy(r => r.Seq).ToList();
        Assert.Equal(["create-token", "revoke-token"], rows.Select(r => r.Entry.Action));
        Assert.All(rows, r => Assert.Equal(("owner", t.Id.ToString(CultureInfo.InvariantCulture)), (r.Entry.User, r.Entry.EntityKey)));
        Assert.Equal("""{"Name":"x","ReadWrite":true}""", rows[0].Entry.After);
        Assert.Equal("""{"Name":"x","ReadWrite":true}""", rows[1].Entry.Before);
        Assert.All(rows, r => Assert.DoesNotContain(t.Token, (r.Entry.Before ?? "") + (r.Entry.After ?? ""), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_busy_host_db_neither_fails_nor_delays_authentication()
    {
        var t = h.Tokens.Create(h.Owner.Context, "x", readWrite: false);
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var writer = Task.Run(() => h.Data.WriteHost(_ => { holding.Set(); release.Wait(); return 0; }));
        holding.Wait();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ctx = Auth(t.Token);   // last_used_at is due, but a save holds the host.db write lock
        watch.Stop();
        release.Set();
        await writer;
        Assert.Equal("x", ctx.TokenName);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"Waited {watch.ElapsedMilliseconds} ms.");
        Assert.Null(Assert.Single(h.Tokens.List(h.Owner.Context)).LastUsedAt);
        Auth(t.Token);
        Assert.NotNull(Assert.Single(h.Tokens.List(h.Owner.Context)).LastUsedAt);
    }

    [Fact]
    public void A_valid_call_from_the_same_address_does_not_clear_the_failed_count()
    {
        var good = h.Tokens.Create(h.Owner.Context, "good", readWrite: false);
        for (var i = 0; i < 4; i++) Fails(ServiceError.NotAuthenticated, () => Auth("ohk_bad"));
        Auth(good.Token);   // another program on the host PC, with a good token
        Fails(ServiceError.NotAuthenticated, () => Auth("ohk_bad"));
        Fails(ServiceError.TooManyAttempts, () => Auth("ohk_bad"));
        Auth(good.Token);
    }
}
