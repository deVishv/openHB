using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Reports;
using ApiMap = OpenHisabKitab.Host.Api.ApiMap;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class StatementTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Ledger(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task Journal(ShellViewModel shell, string type, string dr, string cr, long paise)
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = type, Date = "2026-05-10", Narration = "entry",
                Lines =
                {
                    new PostingLineMsg { Ledger = Ledger(shell, dr), Paise = paise },
                    new PostingLineMsg { Ledger = Ledger(shell, cr), Paise = -paise },
                },
            },
        }, t.Headers());
    }

    static async Task<StatementViewModel> Run(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<StatementViewModel>(shell.Tabs.Active!.Screen);
    }

    static IReadOnlyList<string> Texts(ReportLine line) => [.. line.Cells.Select(c => c.Text)];

    [Fact]
    public async Task Balance_sheet_shows_schedule_iii_by_default_with_previous_year()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", "Cash", "Capital", 100000);
        var s = await Run(shell, "BSHT 31-05-2027");
        Assert.Equal("Balance sheet (Schedule III), as at 31-05-2027", s.Heading);
        Assert.Equal(["Particulars", "31-05-2027", "31-05-2026"], s.Columns.Select(c => c.Header));
        Assert.Equal(["Equity and liabilities", "", ""], Texts(s.Lines[0]));
        Assert.Equal(["Cash and cash equivalents", "1,000.00", "1,000.00"], Texts(s.Lines.Single(l => l.Cells[0].Text == "Cash and cash equivalents")));
        Assert.Contains(s.Lines, l => l.Cells[0].Text == "Assets");
        Assert.Equal(2, s.Lines.Count(l => Texts(l).SequenceEqual(["Total", "1,000.00", "1,000.00"])));

        var first = await Run(shell, "BSHT");
        Assert.Equal("Balance sheet (Schedule III), as at 01-06-2026", first.Heading);
        Assert.Equal(["Particulars", "01-06-2026"], first.Columns.Select(c => c.Header));
    }

    [Fact]
    public async Task Ctrl_g_switches_to_chart_style()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", "Cash", "Capital", 100000);
        var s = await Run(shell, "BSHT");
        Assert.True(s.CanRun("report.style"));
        await s.RunAsync("report.style");
        Assert.Equal(LayoutStyle.Chart, s.Style);
        Assert.Equal("Balance sheet (chart of accounts), as at 01-06-2026", s.Heading);
        var cash = s.Lines.Single(l => l.Cells[0].Text == "Cash");
        Assert.Equal(2, cash.Depth);
        Assert.Equal("LEDG Cash 01-04-2026 01-06-2026", cash.Drill);
    }

    [Fact]
    public async Task Net_loss_is_worded_as_loss_in_brackets()
    {
        var shell = await Shell();
        await Journal(shell, "Payment", "Purchases", "Cash", 40000);
        var s = await Run(shell, "PNLS fy");
        Assert.Equal("Profit and loss (Schedule III), 01-04-2026 to 31-03-2027", s.Heading);
        Assert.Equal(["Net loss", "(400.00)"], Texts(s.Lines[^1]));
        Assert.True(s.Lines[^1].Emphasis);
    }

    [Fact]
    public async Task A_loss_beside_a_previous_profit_has_a_neutral_label()
    {
        var shell = await Shell();
        var company = shell.Context.Company!.Id;
        await t.OtherOwnerClient().Companies.AddYearAsync(new CompanyRef { Company = company }, t.Headers());
        await Journal(shell, "Receipt", "Cash", "Sales", 500000);
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = company,
            Journal = new JournalMsg
            {
                Type = "Payment", Date = "2027-05-10", Narration = "loss",
                Lines =
                {
                    new PostingLineMsg { Ledger = Ledger(shell, "Purchases"), Paise = 40000 },
                    new PostingLineMsg { Ledger = Ledger(shell, "Cash"), Paise = -40000 },
                },
            },
        }, t.Headers());
        var s = await Run(shell, "PNLS 01-04-2027 31-03-2028");
        Assert.Equal(["Net profit / (loss)", "(400.00)", "5,000.00"], Texts(s.Lines[^1]));
    }

    [Fact]
    public async Task Group_line_drills_to_trial_balance_of_that_group()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", "Cash", "Capital", 100000);
        var s = await Run(shell, "BSHT 31-03-2027");
        var capital = s.Lines.Single(l => l.Cells[0].Text == "Capital");
        Assert.Equal("TRLB Capital 01-04-2026 31-03-2027", capital.Drill);
        var cash = s.Lines.Single(l => l.Cells[0].Text == "Cash and cash equivalents");
        Assert.Equal("TRLB Current Assets 01-04-2026 31-03-2027", cash.Drill);

        await s.OpenAsync(capital, newTab: false);
        var tb = Assert.IsType<TrialBalanceViewModel>(shell.Tabs.Active!.Screen);
        Assert.Equal("Capital", tb.Subject);
    }

    [Fact]
    public async Task Viewer_opens_reports_data_entry_sees_the_host_refusal()
    {
        var owner = await Shell();
        var company = owner.Context.Company!.Id;
        Assert.True(Core.Commands.CommandParser.TryParse("BSHT", out var cmd));
        foreach (var (role, message) in new[] { ("Viewer", (string?)null), ("DataEntry", "You do not have permission for this.") })
        {
            var user = await t.UserShellAsync(role.ToLowerInvariant(), role, company);
            await CompanyOpener.OpenAsync(user.Context, new CompanyEntry { Id = company, Name = "A Traders" });
            var s = (StatementViewModel)StatementViewModel.Create(cmd, user.Context)!;
            await s.LoadAsync(CancellationToken.None);
            Assert.Equal(message, s.Message);
        }
    }

    [Fact]
    public void Host_text_matches_client_text_for_fixed_lines()
    {
        foreach (var kind in new[] { LayoutLineKind.CashAndEquivalents, LayoutLineKind.ProfitForPeriod, LayoutLineKind.OpeningDifference,
                     LayoutLineKind.SideTotal, LayoutLineKind.NetProfit })
            foreach (var amount in new[] { new Money(100), new Money(-100) })
                foreach (Money? previous in new Money?[] { null, new Money(100), new Money(-100) })
                {
                    var line = new LayoutLine(LayoutSide.Assets, kind, 0, "", null, null, amount, previous);
                    var host = ApiMap.ToDto(new StatementLayout(LayoutStyle.ScheduleIII, previous is not null, [line])).Lines[0].Label;
                    Assert.Equal(host, StatementViewModel.Wording(kind.ToString(), amount.Paise, previous?.Paise));
                }
    }
}
