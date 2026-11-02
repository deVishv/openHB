using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class TrialBalanceTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Ledger(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task Receipt(ShellViewModel shell, long paise, string date = "2026-05-10")
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = date, Narration = "cash sale",
                Lines =
                {
                    new PostingLineMsg { Ledger = Ledger(shell, "Cash"), Paise = paise },
                    new PostingLineMsg { Ledger = Ledger(shell, "Sales"), Paise = -paise },
                },
            },
        }, t.Headers());
    }

    static async Task<TrialBalanceViewModel> Run(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<TrialBalanceViewModel>(shell.Tabs.Active!.Screen);
    }

    static IReadOnlyList<string> Texts(ReportLine line) => [.. line.Cells.Select(c => c.Text)];

    [Fact]
    public async Task Trial_balance_lists_ledgers_with_dr_cr_and_totals_agree()
    {
        var shell = await Shell();
        await Receipt(shell, 1234560);
        var s = await Run(shell, "TRLB");
        Assert.Equal(["Ledger", "Opening", "Debit", "Credit", "Closing"], s.Columns.Select(c => c.Header));
        Assert.Equal("Trial balance, 01-04-2026 to 01-06-2026", s.Heading);
        Assert.Equal(["Cash", "0.00", "12,345.60", "0.00", "12,345.60 Dr"], Texts(s.Lines.Single(l => l.Cells[0].Text == "Cash")));
        Assert.Equal(["Sales", "0.00", "0.00", "12,345.60", "12,345.60 Cr"], Texts(s.Lines.Single(l => l.Cells[0].Text == "Sales")));
        var total = s.Lines[^1];
        Assert.True(total.Emphasis);
        Assert.Equal(["Total", "", "12,345.60", "12,345.60", ""], Texts(total));
        Assert.Null(total.Drill);
    }

    [Fact]
    public async Task Trial_balance_for_a_group_lists_only_its_ledgers()
    {
        var shell = await Shell();
        await Receipt(shell, 1000);
        var s = await Run(shell, "TRLB current assets");
        Assert.Equal("Trial balance, Current Assets, 01-04-2026 to 01-06-2026", s.Heading);
        Assert.Equal("TRLB Current Assets 2026-04-01 2026-06-01.csv", s.ExportName);
        Assert.Equal(["Cash", "Total for Current Assets"], s.Lines.Select(l => l.Cells[0].Text));
        Assert.Equal("10.00", s.Lines[^1].Cells[2].Text);

        var none = await Run(shell, "TRLB nothing like it");
        Assert.Equal("No group named nothing like it.", none.Message);
        Assert.Empty(none.Lines);
    }

    [Fact]
    public async Task Enter_on_a_ledger_runs_ledg_with_dates()
    {
        var shell = await Shell();
        await Receipt(shell, 1000);
        var s = await Run(shell, "TRLB fy");
        var cash = s.Lines.Single(l => l.Cells[0].Text == "Cash");
        Assert.Equal("LEDG Cash 01-04-2026 31-03-2027", cash.Drill);
    }

    [Fact]
    public async Task Period_across_years_says_one_line()
    {
        var shell = await Shell();
        var s = await Run(shell, "TRLB 01-03-2026 30-04-2026");
        Assert.Equal("Choose a period within one financial year.", s.Message);
        Assert.Empty(s.Lines);
    }

    [Fact]
    public async Task Default_period_is_year_start_to_working_date()
    {
        var shell = await Shell();
        shell.Context.WorkingDate = new DateOnly(2026, 8, 15);
        var s = await Run(shell, "TRLB");
        Assert.Equal("Trial balance, 01-04-2026 to 15-08-2026", s.Heading);
        shell.Context.WorkingDate = new DateOnly(2027, 8, 15);
        s = await Run(shell, "TRLB");
        Assert.Equal("Trial balance, 01-04-2026 to 31-03-2027", s.Heading);
    }

    [Fact]
    public async Task Export_writes_the_lines_and_is_offered_only_with_the_export_right()
    {
        var shell = await Shell();
        await Receipt(shell, 1000);
        var s = await Run(shell, "TRLB");
        Assert.True(s.CanExport);
        Assert.True(s.CanRun("report.export"));
        var asked = false;
        s.ExportRequested += () => asked = true;
        await s.RunAsync("report.export");
        Assert.True(asked);
        Assert.Equal("TRLB 2026-04-01 2026-06-01.csv", s.ExportName);
        using var stream = new MemoryStream();
        s.WriteCsv(stream);
        Assert.Contains("Cash,0.00,10.00,0.00,10.00", System.Text.Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Viewer_cannot_export()
    {
        var owner = await Shell();
        var viewer = await t.UserShellAsync("vera", "Viewer", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(viewer.Context, new CompanyEntry { Id = owner.Context.Company!.Id, Name = "A Traders" });
        Assert.True(Core.Commands.CommandParser.TryParse("TRLB", out var cmd));
        var s = (TrialBalanceViewModel)TrialBalanceViewModel.Create(cmd, viewer.Context)!;
        await s.LoadAsync(CancellationToken.None);
        Assert.Null(s.Message);
        Assert.NotEmpty(s.Lines);
        Assert.False(s.CanExport);
        Assert.False(s.CanRun("report.export"));
    }
}
