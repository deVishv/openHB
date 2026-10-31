using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Tests;
using OpenHisabKitab.Contracts.V1;
using V1Empty = OpenHisabKitab.Contracts.V1.Empty;

namespace OpenHisabKitab.Client.Model.Tests.Connection;

public sealed class HostConnectionTests
{
    static Task<CompanyList> Companies(HostConnection c) => c.CallAsync((h, o) => h.Companies.ListAsync(new V1Empty(), o));

    [Fact]
    public async Task New_host_needs_setup_then_owner_is_connected()
    {
        using var t = new ClientTestHost();
        var c = t.Connection();
        Assert.Equal(ConnectionState.NeedsSetup, await c.ConnectAsync(t.Address));
        await c.SetupOwnerAsync("owner", ClientTestHost.Password);
        Assert.Equal(ConnectionState.Connected, c.State);
        Assert.Equal("owner", c.Session!.UserName);
        Assert.Empty((await Companies(c)).Companies);
    }

    [Fact]
    public async Task Existing_host_needs_login_and_a_wrong_password_shows_the_host_message()
    {
        using var t = new ClientTestHost();
        await t.LoggedInOwnerAsync();
        var c = t.Connection();
        Assert.Equal(ConnectionState.NeedsLogin, await c.ConnectAsync(t.Address));
        var ex = await Assert.ThrowsAsync<HostException>(() => c.LoginAsync("owner", "wrong-password"));
        Assert.Equal(HostErrorKind.NeedsLogin, ex.Kind);
        Assert.Equal(ConnectionState.NeedsLogin, c.State);
        await c.LoginAsync("owner", ClientTestHost.Password);
        Assert.Equal(ConnectionState.Connected, c.State);
    }

    [Fact]
    public async Task Other_version_stops_before_login()
    {
        using var t = new ClientTestHost();
        var c = t.Connection(clientVersion: "9.9.9");
        Assert.Equal(ConnectionState.VersionMismatch, await c.ConnectAsync(t.Address));
        Assert.Equal($"Install Open Hisab Kitab {ProductVersion.Current} to connect.", c.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.LoginAsync("owner", ClientTestHost.Password));
    }

    [Fact]
    public async Task Lost_session_needs_login_then_the_same_call_succeeds()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        var reconnected = 0;
        c.Reconnected += () => reconnected++;
        t.EndAllSessions();
        var ex = await Assert.ThrowsAsync<HostException>(() => Companies(c));
        Assert.Equal((HostErrorKind.NeedsLogin, "Log in again."), (ex.Kind, ex.Message));
        Assert.Equal((ConnectionState.NeedsLogin, "Log in again."), (c.State, c.Message));
        await c.LoginAsync("owner", ClientTestHost.Password);
        Assert.NotNull(await Companies(c));
        Assert.Equal(1, reconnected);
    }

    [Fact]
    public async Task Unreachable_host_retries_with_backoff_and_reports_reconnected()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        var reconnected = 0;
        c.Reconnected += () => reconnected++;
        t.Offline = true;
        var ex = await Assert.ThrowsAsync<HostException>(() => Companies(c));
        Assert.Equal((HostErrorKind.Unreachable, ConnectionState.Reconnecting), (ex.Kind, c.State));
        Assert.Equal("Host not reachable. Trying again.", c.Message);
        await t.Settle(c);
        t.Time.Advance(TimeSpan.FromSeconds(1));
        await t.Settle(c);
        Assert.Equal(ConnectionState.Reconnecting, c.State);
        t.Offline = false;
        t.Time.Advance(TimeSpan.FromSeconds(1));
        await t.Settle(c);
        Assert.Equal(ConnectionState.Reconnecting, c.State);   // the second wait is 2 s
        t.Time.Advance(TimeSpan.FromSeconds(1));
        await t.Settle(c);
        Assert.Equal((ConnectionState.Connected, 1), (c.State, reconnected));
        Assert.NotNull(await Companies(c));
    }

    [Fact]
    public async Task Reconnect_after_a_lost_session_asks_for_login()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        t.Offline = true;
        await Assert.ThrowsAsync<HostException>(() => Companies(c));
        t.EndAllSessions();                    // the host restarted while away
        t.Offline = false;
        await t.Settle(c);
        t.Time.Advance(TimeSpan.FromSeconds(1));
        await t.Settle(c);
        Assert.Equal(ConnectionState.NeedsLogin, c.State);
    }

    [Fact]
    public async Task A_retry_in_flight_during_logout_changes_nothing_after_it()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        var reconnected = 0;
        c.Reconnected += () => reconnected++;
        t.Offline = true;
        await Assert.ThrowsAsync<HostException>(() => Companies(c));
        await t.Settle(c);
        t.Offline = false;
        t.HoldCompanyList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        t.Time.Advance(TimeSpan.FromSeconds(1));                     // the retry sends GetInfo, then Companies.List, which waits
        await ClientTestHost.Until(() => t.Requests.Count(r => r.Path.EndsWith("Companies/List", StringComparison.Ordinal)) >= 1);
        await c.LogoutAsync();
        var hold = t.HoldCompanyList;
        t.HoldCompanyList = null;
        hold.SetResult();                                             // the old attempt now finishes with the old token
        await ClientTestHost.Until(() => !c.Busy);
        await Task.Delay(200);
        Assert.Equal((ConnectionState.NeedsLogin, null, 0), (c.State, c.Message, reconnected));
        await c.LoginAsync("owner", ClientTestHost.Password);
        Assert.Equal(0, reconnected);                                 // a plain login, not a lost session
    }

    [Fact]
    public async Task Conflict_carries_the_current_party()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(c);
        var chart = await c.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o));
        var debtors = chart.Groups.Single(g => g.Name == "Trade Receivables").Id;
        SavePartyRequest Save(int ledger, long version) => new()
        {
            Company = company, Name = "Ravi Stores", Group = debtors,
            Party = new PartyMsg { Ledger = ledger, State = "27", CreditDays = 30, Address = "Pune", RowVersion = version },
        };
        var id = (int)(await c.CallAsync((h, o) => h.Masters.SavePartyAsync(Save(0, 0), o))).Id;
        await c.CallAsync((h, o) => h.Masters.SavePartyAsync(Save(id, 1), o));
        var ex = await Assert.ThrowsAsync<HostException>(() => c.CallAsync((h, o) => h.Masters.SavePartyAsync(Save(id, 1), o)));
        Assert.Equal(HostErrorKind.Conflict, ex.Kind);
        Assert.Equal((id, 2L), (ex.Current(PartyMsg.Parser).Ledger, ex.Current(PartyMsg.Parser).RowVersion));
    }

    [Fact]
    public async Task Refusals_carry_the_host_message()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        var ex = await Assert.ThrowsAsync<HostException>(() => c.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = "A Traders", State = "27", Registration = "Regular", Gstin = "", Address = "Pune", FirstFyStartYear = 2026,
        }, o)));
        Assert.Equal((HostErrorKind.Refused, "Enter the GSTIN."), (ex.Kind, ex.Message));
        Assert.Equal(ConnectionState.Connected, c.State);
    }

    [Fact]
    public async Task Every_call_sends_the_version_and_login_sends_the_machine()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        await Companies(c);
        var setup = t.Requests.Single(r => r.Path.EndsWith("/SetupOwner", StringComparison.Ordinal));
        var list = t.Requests.Last(r => r.Path.EndsWith("/List", StringComparison.Ordinal));
        Assert.Equal((ProductVersion.Current, ClientTestHost.Machine, false), (setup.Version, setup.Machine, setup.HasToken));
        Assert.Equal((ProductVersion.Current, true), (list.Version, list.HasToken));
        Assert.All(t.Requests, r => Assert.True(r.Path.EndsWith("/GetInfo", StringComparison.Ordinal) || r.Version == ProductVersion.Current));
    }

    [Fact]
    public async Task Logout_needs_login_again()
    {
        using var t = new ClientTestHost();
        var c = await t.LoggedInOwnerAsync();
        await c.LogoutAsync();
        Assert.Equal(ConnectionState.NeedsLogin, c.State);
        Assert.Null(c.Session);
    }
}
