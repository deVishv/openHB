using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class DayBookTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static JournalRequest Receipt(int company, ChartMsg chart, string date, string narration) => new()
    {
        Company = company,
        Journal = new JournalMsg
        {
            Type = "Receipt", Date = date, Narration = narration,
            Lines =
            {
                new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Cash").Id, Paise = 25000 },
                new PostingLineMsg { Ledger = chart.Ledgers.Single(l => l.Required == "Sales").Id, Paise = -25000 },
            },
        },
    };

    [Fact]
    public async Task Day_book_defaults_to_the_working_date()
    {
        var shell = await Shell();
        var (company, chart) = (shell.Context.Company!.Id, shell.Context.Lookups.Chart);
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(Receipt(company, chart, "2026-06-01", "today"), t.Headers());
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(Receipt(company, chart, "2026-05-31", "yesterday"), t.Headers());
        await shell.RunAsync("DAYB", newTab: true);
        var s = Assert.IsType<DayBookViewModel>(shell.Tabs.Active!.Screen);
        Assert.Equal("Day book, 01-06-2026 to 01-06-2026", s.Heading);
        Assert.Equal(["Date", "Type", "Number", "Party or ledger", "Amount", "Narration", "Status"], s.Columns.Select(c => c.Header));
        var line = Assert.Single(s.Lines);
        Assert.Equal(["01-06-2026", "RCPT", "RCT/26-27/00001", "", "250.00", "today", ""], line.Cells.Select(c => c.Text));
        Assert.Equal("RCPT RCT/26-27/00001", line.Drill);

        await shell.RunAsync("DAYB fy", newTab: true);
        Assert.Equal(2, ((DayBookViewModel)shell.Tabs.Active!.Screen).Lines.Count);
    }

    [Fact]
    public async Task Data_entry_sees_only_their_own_vouchers_in_day_book()
    {
        var owner = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(owner);
        var chart = await owner.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o));
        await owner.CallAsync((h, o) => h.Vouchers.SaveJournalAsync(Receipt(company, chart, "2026-06-01", "owner"), o));
        var clerk = await t.UserShellAsync("clerk", "DataEntry", company);
        await CompanyOpener.OpenAsync(clerk.Context, new CompanyEntry { Id = company, Name = "A Traders" });
        await clerk.Context.Host.CallAsync((h, o) => h.Vouchers.SaveJournalAsync(Receipt(company, chart, "2026-06-01", "clerk"), o));
        Assert.True(Core.Commands.CommandParser.TryParse("DAYB", out var cmd));
        var s = (DayBookViewModel)DayBookViewModel.Create(cmd, clerk.Context)!;
        await s.LoadAsync(CancellationToken.None);
        Assert.Null(s.Message);
        var line = Assert.Single(s.Lines);
        Assert.Equal("clerk", line.Cells[5].Text);
    }
}
