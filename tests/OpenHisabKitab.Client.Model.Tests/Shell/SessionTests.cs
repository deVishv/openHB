using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using static OpenHisabKitab.Client.Model.Tests.Shell.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Shell;

public sealed class SessionTests
{
    [Fact]
    public async Task First_run_goes_connect_setup_then_company()
    {
        using var t = new ClientTestHost();
        var shell = t.Shell();
        Assert.Equal(SessionPanel.Connect, shell.Session.Panel);
        Assert.Equal("47250", shell.Session.Port);
        shell.Session.HostName = "localhost";
        await shell.Session.ConnectAsync();
        Assert.Equal(SessionPanel.Setup, shell.Session.Panel);
        shell.Session.UserName = "owner";
        await shell.Session.SetupAsync(ClientTestHost.Password, ClientTestHost.Password);
        Assert.Equal(SessionPanel.None, shell.Session.Panel);
        Assert.Equal("1 CMPY", shell.Tabs.Active!.Title);
        Assert.Equal("Connected to localhost:47250", shell.Status.Connection);
        Assert.Equal("localhost", shell.Context.Settings.Current.Text("connection.host"));
    }

    [Fact]
    public async Task Bad_address_names_the_field_and_calls_nothing()
    {
        using var t = new ClientTestHost();
        var shell = t.Shell();
        shell.Session.HostName = "";
        await shell.Session.ConnectAsync();
        Assert.Equal((SessionPanel.Connect, "Enter the host name or IP address."), (shell.Session.Panel, shell.Session.Message));
        Assert.Empty(t.Requests);
    }

    [Fact]
    public async Task Passwords_that_differ_are_refused_before_calling_the_host()
    {
        using var t = new ClientTestHost();
        var shell = t.Shell();
        shell.Session.HostName = "localhost";
        await shell.Session.ConnectAsync();
        var before = t.Requests.Count;
        shell.Session.UserName = "owner";
        await shell.Session.SetupAsync("owner-password-1", "owner-password-2");
        Assert.Equal((SessionPanel.Setup, "The passwords differ."), (shell.Session.Panel, shell.Session.Message));
        Assert.Equal(before, t.Requests.Count);
    }

    [Fact]
    public async Task Wrong_password_shows_the_host_message_and_stays_on_login()
    {
        using var t = new ClientTestHost();
        await t.LoggedInOwnerAsync();
        var shell = t.Shell();
        shell.Session.HostName = "localhost";
        await shell.Session.ConnectAsync();
        shell.Session.UserName = "owner";
        await shell.Session.LoginAsync("wrong-password");
        Assert.Equal(SessionPanel.Login, shell.Session.Panel);
        Assert.False(string.IsNullOrEmpty(shell.Session.Message));
    }

    [Fact]
    public async Task Shell_keys_are_off_while_a_panel_is_up()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["HELP"]);
        t.EndAllSessions();
        await shell.RunAsync("rfsh", false);
        Assert.Equal(SessionPanel.Login, shell.Session.Panel);
        Assert.False(await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext));
        Assert.False(await shell.HandleKeyAsync(Chord("f1"), NoContext));
        Assert.False(shell.AcceptsTyping);
        Assert.Single(shell.Tabs.Tabs);
    }

    [Fact]
    public async Task Lost_session_keeps_tabs_and_marks_them_changed_after_login()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["HELP", "CMPY"]);
        t.EndAllSessions();
        await shell.RunAsync("rfsh", false);
        Assert.Equal((SessionPanel.Login, "owner", 2), (shell.Session.Panel, shell.Session.UserName, shell.Tabs.Tabs.Count));
        Assert.Equal("Log in again.", shell.Status.Connection);
        await shell.Session.LoginAsync(ClientTestHost.Password);
        Assert.Equal(SessionPanel.None, shell.Session.Panel);
        Assert.All(shell.Tabs.Tabs, tab => Assert.True(tab.Changed));
        Assert.True(shell.Status.Changed);
    }

    [Fact]
    public async Task Log_out_clears_tabs_company_and_the_user_layer()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["HELP", "CMPY"], company: "A Traders");
        await shell.Context.Settings.SetAsync("ui.fontSize", "20", Core.Settings.SettingLayer.User, null);
        Assert.True(await shell.LogOutAsync());
        Assert.Empty(shell.Tabs.Tabs);
        Assert.Null(shell.Context.Company);
        Assert.Equal("Open Hisab Kitab", shell.Title);
        Assert.Equal(14, shell.Context.Settings.Current.Int("ui.fontSize"));
        Assert.Equal(SessionPanel.Login, shell.Session.Panel);
    }

    [Fact]
    public async Task Log_out_with_unsaved_edits_asks_and_no_keeps_everything()
    {
        using var t = new ClientTestHost();
        var confirm = new FakeConfirm(answer: false);
        var made = new List<FakeScreen>();
        var shell = await t.LoggedInShellAsync(open: [], built: Fakes(made, true, "SETG", "CMPY"), confirm: confirm);
        await shell.RunAsync("setg", true);
        Assert.False(await shell.LogOutAsync());
        Assert.Equal((1, ConnectionState.Connected), (shell.Tabs.Tabs.Count, shell.Context.Host.State));
    }

    [Fact]
    public async Task Another_user_after_a_lost_session_gets_a_clean_shell()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["HELP"], company: "A Traders");
        var id = shell.Context.Company!.Id;
        var admin = t.OtherOwnerClient().Admin;
        var asha = (int)(await admin.CreateUserAsync(new CreateUserRequest { Name = "asha", Password = ClientTestHost.Password, Role = "Viewer" }, t.Headers())).Id;
        await admin.SetUserCompaniesAsync(new SetUserCompaniesRequest { UserId = asha, Companies = { id } }, t.Headers());
        t.EndAllSessions();
        await shell.RunAsync("rfsh", false);
        Assert.Equal(SessionPanel.Login, shell.Session.Panel);
        shell.Session.UserName = "asha";
        await shell.Session.LoginAsync(ClientTestHost.Password);
        Assert.Equal(SessionPanel.None, shell.Session.Panel);
        Assert.DoesNotContain(shell.Tabs.Tabs, tab => tab.Title == "1 HELP");
        Assert.Equal("asha", shell.Context.Host.Session!.UserName);
    }

    [Fact]
    public async Task Several_load_errors_say_how_many_more()
    {
        using var t = new ClientTestHost();
        File.WriteAllText(Path.Combine(t.ClientFolder, "settings.json"), "{ \"ui.fontSize\": 99, \"ui.zoom\": 1, \"ui.fontFamily\": \"Arial\" }");
        var shell = await t.LoggedInShellAsync(open: []);
        Assert.EndsWith("(and 2 more. See SETG.)", shell.Status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_change_marks_an_affected_tab()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["CMPY"], company: "A Traders");
        await t.OtherOwnerClient().Companies.SetSettingAsync(
            new SettingRequest { Company = shell.Context.Company!.Id, Key = "invoice.roundOff", Json = "false" }, t.Headers());
        await t.Eventually(() => shell.Tabs.Active!.Changed && !shell.Context.Settings.Current.Bool("invoice.roundOff"));
        Assert.True(shell.Status.Changed);
    }

    [Fact]
    public async Task Auto_reload_reloads_instead_of_marking()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["CMPY"], company: "A Traders");
        await shell.Context.Settings.SetAsync("reports.autoReload", "true", Core.Settings.SettingLayer.Machine, null);
        var screen = (FakeScreen)shell.Tabs.Active!.Screen;
        var loads = screen.Loads;
        await t.OtherOwnerClient().Companies.SetSettingAsync(
            new SettingRequest { Company = shell.Context.Company!.Id, Key = "invoice.roundOff", Json = "false" }, t.Headers());
        await t.Eventually(() => screen.Loads > loads);
        Assert.False(shell.Tabs.Active!.Changed);
    }

    [Fact]
    public async Task Changes_to_another_company_are_ignored()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["CMPY"], company: "A Traders");
        var other = await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        await t.OtherOwnerClient().Companies.SetSettingAsync(new SettingRequest { Company = other, Key = "invoice.roundOff", Json = "false" }, t.Headers());
        await t.OtherOwnerClient().Companies.SetSettingAsync(
            new SettingRequest { Company = shell.Context.Company!.Id, Key = "invoice.roundOff", Json = "true" }, t.Headers());
        await t.Eventually(() => shell.Tabs.Active!.Changed);
        Assert.True(shell.Context.Settings.Current.Bool("invoice.roundOff"));
    }

    [Fact]
    public async Task Unreachable_shows_trying_again_in_status()
    {
        using var t = new ClientTestHost();
        var shell = await t.LoggedInShellAsync(open: ["HELP"]);
        t.Offline = true;
        await shell.RunAsync("rfsh", false);
        Assert.Equal(SessionPanel.None, shell.Session.Panel);
        Assert.Equal("Host not reachable. Trying again.", shell.Status.Connection);
        Assert.True(await shell.HandleKeyAsync(Chord("ctrl+w"), NoContext));
    }

    [Fact]
    public async Task Load_error_shows_in_status()
    {
        using var t = new ClientTestHost();
        File.WriteAllText(Path.Combine(t.ClientFolder, "settings.json"), "{ \"ui.fontSize\": 99 }");
        var shell = await t.LoggedInShellAsync(open: []);
        Assert.Matches(@"^settings\.json line \d+: Setting ui\.fontSize must be a whole number from 12 to 28\.$", shell.Status.Message);
        // Saving the host address on connect kept the bad entry, so the user still sees and fixes it.
        Assert.Contains("99", File.ReadAllText(Path.Combine(t.ClientFolder, "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Last_company_reopens_after_login()
    {
        using var t = new ClientTestHost();
        await t.LoggedInShellAsync(open: [], company: "A Traders");
        var again = t.Shell();
        again.Session.HostName = "localhost";
        await again.Session.ConnectAsync();
        again.Session.UserName = "owner";
        await again.Session.LoginAsync(ClientTestHost.Password);
        Assert.Equal("Open Hisab Kitab: A Traders FY 2026-27", again.Title);
        Assert.Empty(again.Tabs.Tabs);
    }

    [Fact]
    public async Task Working_date_shows_in_status_in_the_chosen_format()
    {
        using var t = new ClientTestHost();
        var shell = t.Shell();
        Assert.Equal("Date 01-06-2026", shell.Status.WorkingDate);
        await shell.Context.Settings.SetAsync("ui.dateFormat", "\"yyyy-mm-dd\"", Core.Settings.SettingLayer.Machine, null);
        shell.Context.WorkingDate = new DateOnly(2026, 4, 15);
        Assert.Equal("Date 2026-04-15", shell.Status.WorkingDate);
    }

    [Fact]
    public async Task Change_host_returns_to_the_connect_panel()
    {
        using var t = new ClientTestHost();
        var shell = t.Shell();
        shell.Session.HostName = "localhost";
        await shell.Session.ConnectAsync();
        shell.Session.ChangeHost();
        Assert.Equal(SessionPanel.Connect, shell.Session.Panel);
        Assert.Equal(ConnectionState.NeedsSetup, shell.Context.Host.State);
    }
}
