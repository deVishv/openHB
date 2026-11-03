using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class AuditTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    async Task Receipt(ShellViewModel shell, string narration)
    {
        var chart = shell.Context.Lookups.Chart;
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10", Narration = narration,
                Lines =
                {
                    new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Cash").Id, Paise = 100 },
                    new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Sales").Id, Paise = -100 },
                },
            },
        }, t.Headers());
    }

    static async Task<AuditViewModel> Run(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<AuditViewModel>(shell.Tabs.Active!.Screen);
    }

    static async Task<AuditViewModel> Direct(ShellViewModel shell, string command)
    {
        Assert.True(Core.Commands.CommandParser.TryParse(command, out var cmd));
        var s = (AuditViewModel)AuditViewModel.Create(cmd, shell.Context)!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    [Fact]
    public async Task Lists_the_company_trail_newest_first()
    {
        var shell = await Shell();
        await Receipt(shell, "first");
        await Receipt(shell, "second");
        var s = await Run(shell, "AUDT");
        Assert.True(s.Rows.Count >= 2);
        Assert.True(s.Rows[0].Seq > s.Rows[1].Seq);
        Assert.Equal(("create", "voucher", "owner", "App"), (s.Rows[0].Action, s.Rows[0].Entity, s.Rows[0].User, s.Rows[0].Source));
        Assert.Contains("second", s.Rows[0].After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filter_by_user_and_dates()
    {
        var shell = await Shell();
        await Receipt(shell, "one");
        var s = await Run(shell, "AUDT");
        s.User = "nobody";
        await s.SearchAsync();
        Assert.Empty(s.Rows);

        s.User = "OWNER";
        s.From = "01-01-2020";
        s.To = "31-12-2099";
        await s.SearchAsync();
        Assert.NotEmpty(s.Rows);

        s.From = "01-01-2099";
        await s.SearchAsync();
        Assert.Empty(s.Rows);

        s.From = "not a date";
        await s.SearchAsync();
        Assert.Equal("Enter the date as dd-mm-yyyy.", s.Message);
    }

    [Fact]
    public async Task Enter_shows_before_and_after()
    {
        var shell = await Shell();
        await Receipt(shell, "shown");
        var s = await Run(shell, "AUDT");
        s.Selected = s.Rows.First(r => r.Entity == "voucher");
        Assert.Equal("", s.Before);
        Assert.Contains("\n", s.After, StringComparison.Ordinal);
        Assert.Contains("shown", s.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_page_says_older_records_are_not_shown_and_a_reload_keeps_the_selection()
    {
        var shell = await Shell();
        for (var i = 0; i < 500; i++) await Receipt(shell, $"r{i}");
        var s = await Run(shell, "AUDT");
        Assert.Equal(500, s.Rows.Count);
        Assert.Equal("Showing the newest 500 records. Narrow the dates or user to see older ones.", s.Message);
        var picked = s.Rows[3].Seq;
        s.Selected = s.Rows[3];
        await s.LoadAsync(CancellationToken.None);
        Assert.Equal(picked, s.Selected?.Seq);
    }

    [Fact]
    public async Task Verify_reports_an_intact_chain()
    {
        var shell = await Shell();
        await Receipt(shell, "one");
        var s = await Run(shell, "AUDT");
        await s.VerifyAsync();
        Assert.Matches(@"^Chain intact: \d+ records, anchor matches\.$", s.Verification);
    }

    [Fact]
    public async Task Host_trail_needs_manage_host_settings()
    {
        var owner = await Shell();
        var host = await Run(owner, "AUDT host");
        Assert.Null(host.Message);
        Assert.NotEmpty(host.Rows);
        Assert.Equal("host", host.Subject);

        var accountant = await t.UserShellAsync("acct", "Accountant", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(accountant.Context, new CompanyEntry { Id = owner.Context.Company!.Id, Name = "A Traders" });
        Assert.Equal("You do not have permission for this.", (await Direct(accountant, "AUDT host")).Message);
        Assert.Null((await Direct(accountant, "AUDT")).Message);
    }

    [Fact]
    public async Task Data_entry_sees_the_refusal()
    {
        var owner = await Shell();
        var clerk = await t.UserShellAsync("clerk", "DataEntry", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(clerk.Context, new CompanyEntry { Id = owner.Context.Company!.Id, Name = "A Traders" });
        var s = await Direct(clerk, "AUDT");
        Assert.Equal("You do not have permission for this.", s.Message);
        Assert.Empty(s.Rows);
    }
}
