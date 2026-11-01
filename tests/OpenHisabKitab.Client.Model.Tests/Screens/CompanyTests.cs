using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class CompanyTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<IScreen?> Cmpy(ShellContext ctx, string input)
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var screen = await CompanyViewModel.CreateAsync(cmd, ctx);
        if (screen is not null) await screen.LoadAsync(CancellationToken.None);
        return screen;
    }

    [Fact]
    public async Task Open_sets_the_title_and_remembers_the_company()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        Assert.Equal(["A Traders", "B Traders"], screen.Companies.Select(c => c.Name));
        screen.Selected = screen.Companies[1];
        await screen.OpenAsync();
        Assert.Equal("Open Hisab Kitab: B Traders FY 2026-27", shell.Title);
        Assert.Equal("Opened B Traders FY 2026-27.", shell.Status.Message);
        Assert.Equal(screen.Companies[1].Id, shell.Context.Files.LoadState().LastCompanyByHost[t.Address.ToString()]);
    }

    [Fact]
    public async Task Selecting_a_company_lists_its_years()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        Assert.Equal(screen.Companies[0], screen.Selected);
        await t.Eventually(() => screen.Years.Count == 1);
        Assert.Equal("2026-27", screen.Years[0].Label);
    }

    [Fact]
    public async Task Cmpy_with_a_unique_prefix_opens_without_a_tab()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        Assert.Null(await Cmpy(shell.Context, "CMPY b tr"));
        Assert.Equal("B Traders", shell.Context.Company!.Name);
        Assert.Equal("Opened B Traders FY 2026-27.", shell.Status.Message);
    }

    [Fact]
    public async Task Cmpy_with_an_ambiguous_name_lists_only_the_candidates()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        await t.NewCompanyAsync(shell.Context.Host, "C Stores");
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY traders"))!;
        Assert.Equal(["A Traders", "B Traders"], screen.Companies.Select(c => c.Name));
        Assert.Null(shell.Context.Company);
    }

    [Fact]
    public async Task Cmpy_with_an_unknown_name_offers_create_with_the_name()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY Ravi Stores"))!;
        Assert.True(screen.CanCreate);
        Assert.Equal("Ravi Stores", screen.NewName);
        Assert.True(screen.HasUnsavedEdits);
        Assert.Equal(2026, screen.NewFirstYear);
        Assert.Contains(screen.States, s => s.Label == "27 Maharashtra");
    }

    [Fact]
    public async Task Create_opens_the_new_company()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        screen.NewName = "Ravi Stores";
        screen.NewState = screen.States.Single(s => s.Code == "27");
        screen.NewRegistration = "Unregistered";
        screen.NewAddress = "Pune";
        await screen.SaveAsync();
        Assert.Null(screen.Message);
        Assert.Equal("Open Hisab Kitab: Ravi Stores FY 2026-27", shell.Title);
        Assert.Equal("", screen.NewName);
        Assert.False(screen.HasUnsavedEdits);
        Assert.Contains(screen.Companies, c => c.Name == "Ravi Stores");
    }

    [Fact]
    public async Task Regular_without_gstin_shows_the_host_message_and_keeps_the_form()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        screen.NewName = "Ravi Stores";
        screen.NewState = screen.States.Single(s => s.Code == "27");
        screen.NewRegistration = "Regular";
        await screen.SaveAsync();
        Assert.Equal("Enter the GSTIN.", screen.Message);
        Assert.Equal(("Ravi Stores", "Regular"), (screen.NewName, screen.NewRegistration));
        Assert.Null(shell.Context.Company);
    }

    [Fact]
    public async Task Unreachable_create_keeps_the_form()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        screen.NewName = "Ravi Stores";
        screen.NewState = screen.States.Single(s => s.Code == "27");
        screen.NewRegistration = "Unregistered";
        t.Offline = true;
        await screen.SaveAsync();
        Assert.Equal("Host not reachable. Trying again.", screen.Message);
        Assert.Equal("Ravi Stores", screen.NewName);
        Assert.True(screen.HasUnsavedEdits);
    }

    [Fact]
    public async Task Viewer_sees_no_create_form_and_cannot_add_a_year()
    {
        var owner = await t.LoggedInShellAsync(open: []);
        var id = await t.NewCompanyAsync(owner.Context.Host, "A Traders");
        var viewer = await t.UserShellAsync("viewer", "Viewer", id);
        var screen = (CompanyViewModel)(await Cmpy(viewer.Context, "CMPY"))!;
        Assert.False(screen.CanCreate);
        Assert.False(screen.CanAddYear);
        Assert.Equal(["A Traders"], screen.Companies.Select(c => c.Name));
        var unknown = (CompanyViewModel)(await Cmpy(viewer.Context, "CMPY Ravi"))!;
        Assert.Equal("", unknown.NewName);
    }

    [Fact]
    public async Task Add_year_then_working_date_in_it_opens_that_year()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        await t.Eventually(() => screen.Years.Count == 1);
        await screen.AddYearAsync();
        Assert.Equal(["2026-27", "2027-28"], screen.Years.Select(y => y.Label));
        shell.Context.WorkingDate = new DateOnly(2027, 5, 1);
        await screen.OpenAsync();
        Assert.Equal(new FinancialYear(2027), shell.Context.Year);
        Assert.Equal("Open Hisab Kitab: A Traders FY 2027-28", shell.Title);
    }

    [Fact]
    public async Task Enter_twice_while_a_lookup_runs_routes_once()
    {
        var shell = await t.LoggedInShellAsync(open: [], built: ScreenCatalog.Built());
        await t.NewCompanyAsync(shell.Context.Host, "A Traders");
        t.HoldCompanyList = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = shell.RunAsync("cmpy ravi", newTab: true);
        var second = shell.RunAsync("cmpy ravi", newTab: true);
        await second;
        t.HoldCompanyList.SetResult();
        t.HoldCompanyList = null;
        await first;
        Assert.Single(shell.Tabs.Tabs);
    }

    [Fact]
    public async Task Reloads_on_settings_and_restore_only()
    {
        var shell = await t.LoggedInShellAsync(open: []);
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY Ravi"))!;
        Assert.True(screen.HasUnsavedEdits);
        Assert.True(screen.IsAffectedBy(new ChangeEventMsg { Company = 1, Kind = "Restore" }));
        Assert.False(screen.IsAffectedBy(new ChangeEventMsg { Company = 1, Kind = "Voucher" }));
    }
    [Fact]
    public async Task Edit_renames_the_company_and_the_title()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        screen.Selected = screen.Companies.Single(c => c.Name == "A Traders");
        await screen.EditSelectedAsync();
        Assert.True(screen.Editing);
        Assert.Equal(("A Traders", "27", 2026), (screen.NewName, screen.NewState!.Code, screen.NewFirstYear));
        screen.NewName = "A Traders LLP";
        await screen.SaveAsync();
        Assert.Null(screen.Message);
        Assert.Equal("Open Hisab Kitab: A Traders LLP FY 2026-27", shell.Title);
        Assert.Contains(screen.Companies, c => c.Name == "A Traders LLP");
        await screen.RunAsync("record.new");
        Assert.False(screen.Editing);
        Assert.Equal("", screen.NewName);
    }

    [Fact]
    public async Task Edit_shows_the_host_refusal_for_a_state_change_after_vouchers()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var company = shell.Context.Company!.Id;
        var chart = shell.Context.Lookups.Chart;
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = company,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10",
                Lines =
                {
                    new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Cash").Id, Paise = 100 },
                    new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Sales").Id, Paise = -100 },
                },
            },
        }, t.Headers());
        var screen = (CompanyViewModel)(await Cmpy(shell.Context, "CMPY"))!;
        screen.Selected = screen.Companies.Single();
        await screen.EditSelectedAsync();
        screen.NewState = screen.States.Single(s => s.Code == "29");
        screen.NewGstin = "";
        screen.NewRegistration = "Unregistered";
        await screen.SaveAsync();
        Assert.Equal("State and registration cannot change after vouchers are entered.", screen.Message);
    }
}
