using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class ReportPrintScreenTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Ledger(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task Receipt(ShellViewModel shell, long paise)
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10", Narration = "cash sale",
                Lines =
                {
                    new PostingLineMsg { Ledger = Ledger(shell, "Cash"), Paise = paise },
                    new PostingLineMsg { Ledger = Ledger(shell, "Sales"), Paise = -paise },
                },
            },
        }, t.Headers());
    }

    static async Task<T> Run<T>(ShellViewModel shell, string command) where T : ReportScreen
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<T>(shell.Tabs.Active!.Screen);
    }

    static IEnumerable<string> Texts(PrintDocument d) => d.Pages.SelectMany(p => p.Items).OfType<PrintText>().Select(x => x.Text);

    [Fact]
    public async Task Amounts_keep_screen_text_with_dr_cr()
    {
        var shell = await Shell();
        await Receipt(shell, 1234560);
        var s = await Run<TrialBalanceViewModel>(shell, "TRLB");
        var result = await s.PrintAsync(PrintTarget.Pdf);
        Assert.Null(result.Refusal);
        Assert.Equal("TRLB 2026-04-01 2026-06-01.pdf", result.FileName);
        var texts = Texts(result.Document!).ToList();
        Assert.Contains("12,345.60 Dr", texts);
        Assert.Contains("12,345.60 Cr", texts);
        Assert.Contains("Trial balance, 01-04-2026 to 01-06-2026", texts);
        Assert.Contains("A Traders", texts);
        Assert.False(result.Document!.Paper.IsLandscape);
    }

    [Fact]
    public async Task Viewer_cannot_print_reports()
    {
        var owner = await Shell();
        var viewer = await t.UserShellAsync("vera", "Viewer", owner.Context.Company!.Id);
        await CompanyOpener.OpenAsync(viewer.Context, new CompanyEntry { Id = owner.Context.Company!.Id, Name = "A Traders" });
        Assert.True(Core.Commands.CommandParser.TryParse("TRLB", out var cmd));
        var s = (TrialBalanceViewModel)TrialBalanceViewModel.Create(cmd, viewer.Context)!;
        await s.LoadAsync(CancellationToken.None);
        Assert.NotEmpty(s.Lines);
        foreach (var target in new[] { PrintTarget.Paper, PrintTarget.Pdf })
        {
            var result = await s.PrintAsync(target);
            Assert.Null(result.Document);
            Assert.Equal("Printing reports needs the export right.", result.Refusal);
        }
    }

    [Fact]
    public async Task Print_loads_every_page_first()
    {
        var shell = await Shell();
        var (cash, sales) = (Ledger(shell, "Cash"), Ledger(shell, "Sales"));
        for (var i = 0; i < 503; i++)
        {
            await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
            {
                Company = shell.Context.Company!.Id,
                Journal = new JournalMsg
                {
                    Type = "Receipt", Date = "2026-05-10", Narration = "",
                    Lines = { new PostingLineMsg { Ledger = cash, Paise = 100 }, new PostingLineMsg { Ledger = sales, Paise = -100 } },
                },
            }, t.Headers());
        }
        var s = await Run<LedgerViewModel>(shell, "LEDG Cash fy");
        Assert.True(s.HasMore);
        var result = await s.PrintAsync(PrintTarget.Paper);
        Assert.False(s.HasMore);
        Assert.Equal(1 + 503 + 1, s.Lines.Count);
        var texts = Texts(result.Document!).ToList();
        Assert.Contains("Closing balance", texts);
        Assert.Contains("503.00 Dr", texts);
        Assert.Equal(503, texts.Count(x => x.StartsWith("RCT/26-27/", StringComparison.Ordinal)));
    }
}
