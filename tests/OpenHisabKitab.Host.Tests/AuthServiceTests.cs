namespace OpenHisabKitab.Host.Tests;

public sealed class AuthServiceTests : IDisposable
{
    readonly TestHost host = new();

    public void Dispose() => host.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    [Fact]
    public void Needs_setup_until_owner_exists()
    {
        Assert.True(host.Auth.NeedsSetup());
        _ = host.Owner;
        Assert.False(host.Auth.NeedsSetup());
    }

    [Fact]
    public void Setup_from_another_pc_is_refused()
    {
        var ex = Fails(ServiceError.PermissionDenied, () => host.Auth.SetupOwner("owner", TestHost.OwnerPassword, "PC2", isLoopback: false));
        Assert.Equal("Set up the Owner on the host PC.", ex.Message);
        Assert.True(host.Auth.NeedsSetup());
    }

    [Fact]
    public void Setup_twice_is_refused()
    {
        _ = host.Owner;
        var ex = Fails(ServiceError.Invalid, () => host.Auth.SetupOwner("other", TestHost.OwnerPassword, "HOSTPC", isLoopback: true));
        Assert.Equal("The Owner is already set up.", ex.Message);
    }

    [Fact]
    public void Short_password_is_refused()
    {
        var ex = Fails(ServiceError.Invalid, () => host.Auth.SetupOwner("owner", "short", "HOSTPC", isLoopback: true));
        Assert.Equal("Use at least 8 characters.", ex.Message);
    }

    [Fact]
    public void Login_before_setup_says_so()
    {
        var ex = Fails(ServiceError.SetupRequired, () => host.Auth.Login("owner", "whatever-1", "PC2"));
        Assert.Equal("Set up the Owner first.", ex.Message);
    }

    [Fact]
    public void Owner_logs_in_and_gets_every_permission()
    {
        _ = host.Owner;
        var s = host.Auth.Login("OWNER", TestHost.OwnerPassword, "PC2");
        Assert.Equal("owner", s.Context.UserName);
        Assert.Equal("PC2", s.Context.Machine);
        Assert.Equal(ChangeSource.App, s.Context.Source);
        Assert.Equal(Enum.GetValues<Permission>().ToHashSet(), s.Context.Permissions.ToHashSet());
        Assert.Equal(s.Context, host.Sessions.Resolve(s.Token));
    }

    [Fact]
    public void Wrong_password_gives_one_message()
    {
        _ = host.Owner;
        var ex = Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", "wrong-password", "PC2"));
        Assert.Equal("Wrong user name or password.", ex.Message);
        var unknown = Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("nobody", "wrong-password", "PC2"));
        Assert.Equal(ex.Message, unknown.Message);
    }

    [Fact]
    public void Sixth_failure_is_throttled_until_the_wait_passes()
    {
        _ = host.Owner;
        for (var i = 0; i < 5; i++) Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", "wrong-password", "PC2"));
        var ex = Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", TestHost.OwnerPassword, "PC2"));
        Assert.Equal("Too many attempts. Try again in 30 seconds.", ex.Message);
        host.Time.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("owner", host.Auth.Login("owner", TestHost.OwnerPassword, "PC2").Context.UserName);
    }

    [Fact]
    public void Throttle_wait_doubles()
    {
        _ = host.Owner;
        for (var i = 0; i < 5; i++) Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", "wrong-password", "PC2"));
        host.Time.Advance(TimeSpan.FromSeconds(31));
        Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", "wrong-password", "PC2"));
        var ex = Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", TestHost.OwnerPassword, "PC2"));
        Assert.Equal("Too many attempts. Try again in 60 seconds.", ex.Message);
    }

    [Fact]
    public void Session_expires_after_twelve_idle_hours()
    {
        var s = host.Owner;
        host.Time.Advance(TimeSpan.FromHours(6));
        Assert.NotNull(host.Sessions.Resolve(s.Token));
        host.Time.Advance(TimeSpan.FromHours(11));
        Assert.NotNull(host.Sessions.Resolve(s.Token));
        host.Time.Advance(TimeSpan.FromHours(12));
        Assert.Null(host.Sessions.Resolve(s.Token));
    }

    [Fact]
    public void Logout_ends_the_session()
    {
        var s = host.Owner;
        host.Auth.Logout(s.Token);
        Assert.Null(host.Sessions.Resolve(s.Token));
    }

    [Fact]
    public void Change_password_ends_other_sessions_only()
    {
        var first = host.Owner;
        var second = host.Auth.Login("owner", TestHost.OwnerPassword, "PC2");
        host.Auth.ChangePassword(first.Context, first.Token, TestHost.OwnerPassword, "new-password-1");
        Assert.NotNull(host.Sessions.Resolve(first.Token));
        Assert.Null(host.Sessions.Resolve(second.Token));
        Fails(ServiceError.NotAuthenticated, () => host.Auth.Login("owner", TestHost.OwnerPassword, "PC2"));
        Assert.NotNull(host.Auth.Login("owner", "new-password-1", "PC2"));
    }

    [Fact]
    public void Change_password_with_wrong_current_is_refused()
    {
        var s = host.Owner;
        var ex = Fails(ServiceError.NotAuthenticated, () => host.Auth.ChangePassword(s.Context, s.Token, "wrong-password", "new-password-1"));
        Assert.Equal("Current password is wrong.", ex.Message);
    }

    [Fact]
    public void Data_entry_lacks_reports()
    {
        var s = host.NewUser("asha", BuiltInRole.DataEntry);
        Assert.DoesNotContain(Permission.ViewReports, s.Context.Permissions);
        Fails(ServiceError.PermissionDenied, () => s.Context.Require(Permission.ViewReports));
        s.Context.Require(Permission.CreateVoucher);
    }

    [Fact]
    public void Read_only_context_is_denied_writes_but_not_reads()
    {
        var ctx = host.Owner.Context with { ReadOnly = true };
        var ex = Fails(ServiceError.PermissionDenied, () => ctx.Require(Permission.CreateVoucher));
        Assert.Equal("You do not have permission for this.", ex.Message);
        ctx.Require(Permission.ViewReports);
        ctx.Require(Permission.Export);
    }

    [Fact]
    public void Accountant_cannot_create_users()
    {
        var s = host.NewUser("ravi", BuiltInRole.Accountant);
        Fails(ServiceError.PermissionDenied, () => host.Users.Create(s.Context, "x", "password-x", BuiltInRole.Viewer));
    }

    [Fact]
    public void Duplicate_user_is_refused()
    {
        host.NewUser("ravi", BuiltInRole.Viewer);
        var ex = Fails(ServiceError.Invalid, () => host.Users.Create(host.Owner.Context, "RAVI", "password-x", BuiltInRole.Viewer));
        Assert.Equal("User RAVI already exists.", ex.Message);
    }

    [Fact]
    public void Company_access_is_checked_but_owner_sees_all()
    {
        var s = host.NewUser("ravi", BuiltInRole.Viewer);
        Fails(ServiceError.PermissionDenied, () => s.Context.RequireCompany(1));
        host.Owner.Context.RequireCompany(1);
    }

    [Fact]
    public void Roles_follow_the_spec()
    {
        Assert.Equal(Enum.GetValues<Permission>().ToHashSet(), Roles.Of(BuiltInRole.Owner).ToHashSet());
        var accountant = Roles.Of(BuiltInRole.Accountant);
        Assert.DoesNotContain(Permission.ManageUsers, accountant);
        Assert.DoesNotContain(Permission.UnlockPeriod, accountant);
        Assert.DoesNotContain(Permission.Restore, accountant);
        Assert.DoesNotContain(Permission.ManageHostSettings, accountant);
        Assert.Contains(Permission.LockPeriod, accountant);
        Assert.Equal(new HashSet<Permission> { Permission.EditMasters, Permission.CreateVoucher, Permission.EditVoucher },
            Roles.Of(BuiltInRole.DataEntry).ToHashSet());
        Assert.Equal(new HashSet<Permission> { Permission.ViewAllVouchers, Permission.ViewReports, Permission.ViewGstReports },
            Roles.Of(BuiltInRole.Viewer).ToHashSet());
    }

    [Fact]
    public void Host_audit_records_setup_and_user_creation_without_secrets()
    {
        _ = host.Owner;
        host.NewUser("ravi", BuiltInRole.Viewer);
        using var c = host.Data.OpenHost();
        var rows = AuditLog.Read(c, new AuditFilter());
        Assert.Contains(rows, r => r.Entry.Action == "setup-owner" && r.Entry.User == "owner" && r.Entry.Machine == "HOSTPC");
        Assert.Contains(rows, r => r.Entry.Action == "create" && r.Entry.Entity == "user" && r.Entry.After!.Contains("ravi"));
        Assert.DoesNotContain(rows, r => (r.Entry.After ?? "").Contains("pbkdf2"));
        Assert.True(AuditLog.Verify(c).Ok);
    }
}
