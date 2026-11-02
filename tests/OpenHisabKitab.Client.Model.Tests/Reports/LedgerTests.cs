using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class LedgerTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Ledger(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task Journal(ShellViewModel shell, string type, int dr, int cr, long paise, string date = "2026-05-10")
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = type, Date = date, Narration = "entry",
                Lines = { new PostingLineMsg { Ledger = dr, Paise = paise }, new PostingLineMsg { Ledger = cr, Paise = -paise } },
            },
        }, t.Headers());
    }

    async Task<int> AddLedger(ShellViewModel shell, string name, string kind)
    {
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == kind).Id;
        var id = (int)(await t.OtherOwnerClient().Masters.AddLedgerAsync(new AddLedgerRequest
        {
            Company = shell.Context.Company!.Id, Name = name, Group = group,
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Chart.Ledgers.Any(l => l.Id == id));
        return id;
    }

    static async Task<T> Run<T>(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<T>(shell.Tabs.Active!.Screen);
    }

    static IReadOnlyList<string> Texts(ReportLine line) => [.. line.Cells.Select(c => c.Text)];

    [Fact]
    public async Task Ledger_shows_opening_lines_and_closing_with_running_balance()
    {
        var shell = await Shell();
        var (cash, sales) = (Ledger(shell, "Cash"), Ledger(shell, "Sales"));
        await Journal(shell, "Receipt", cash, sales, 100000);
        var s = await Run<LedgerViewModel>(shell, "LEDG cash fy");
        Assert.Equal(["Date", "Particulars", "Type", "Number", "Debit", "Credit", "Balance"], s.Columns.Select(c => c.Header));
        Assert.Equal("Cash, 01-04-2026 to 31-03-2027", s.Heading);
        Assert.Equal("Cash", s.Subject);
        Assert.Equal(["", "Opening balance", "", "", "", "", "0.00"], Texts(s.Lines[0]));
        Assert.Equal(["10-05-2026", "Sales", "RCPT", "RCT/26-27/00001", "1,000.00", "", "1,000.00 Dr"], Texts(s.Lines[1]));
        Assert.Equal(["", "Closing balance", "", "", "", "", "1,000.00 Dr"], Texts(s.Lines[^1]));
        Assert.Equal("RCPT RCT/26-27/00001", s.Lines[1].Drill);
    }

    [Fact]
    public async Task Ledger_pages_continue_the_balance()
    {
        var shell = await Shell();
        var (cash, sales) = (Ledger(shell, "Cash"), Ledger(shell, "Sales"));
        for (var i = 0; i < 503; i++) await Journal(shell, "Receipt", cash, sales, 100);
        var s = await Run<LedgerViewModel>(shell, "LEDG Cash fy");
        Assert.True(s.HasMore);
        Assert.Equal(501, s.Lines.Count);
        Assert.Equal("500.00 Dr", s.Lines[^1].Cells[6].Text);
        await s.MoreAsync();
        Assert.False(s.HasMore);
        Assert.Equal("501.00 Dr", s.Lines[501].Cells[6].Text);
        Assert.Equal(["", "Closing balance", "", "", "", "", "503.00 Dr"], Texts(s.Lines[^1]));
        Assert.Equal(1 + 503 + 1, s.Lines.Count);
    }

    [Fact]
    public async Task Export_of_a_paged_ledger_writes_every_line_and_the_closing()
    {
        var shell = await Shell();
        var (cash, sales) = (Ledger(shell, "Cash"), Ledger(shell, "Sales"));
        for (var i = 0; i < 503; i++) await Journal(shell, "Receipt", cash, sales, 100);
        var s = await Run<LedgerViewModel>(shell, "LEDG Cash fy");
        Assert.True(s.HasMore);
        await s.RunAsync("report.export");
        Assert.False(s.HasMore);
        using var stream = new MemoryStream();
        s.WriteCsv(stream);
        var rows = System.Text.Encoding.UTF8.GetString(stream.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 1 + 503 + 1, rows.Length);
        Assert.StartsWith(",Closing balance,", rows[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ledger_name_ending_in_a_month_word_reads_the_dates()
    {
        var shell = await Shell();
        var odd = await AddLedger(shell, "Sales apr", "RevenueFromOperations");
        await Journal(shell, "Receipt", Ledger(shell, "Cash"), odd, 5000, "2026-07-01");
        var s = await Run<LedgerViewModel>(shell, "LEDG Sales apr 01-04-2026 31-03-2027");
        Assert.Equal("Sales apr, 01-04-2026 to 31-03-2027", s.Heading);
        Assert.Equal("01-07-2026", s.Lines[1].Cells[0].Text);

        var april = await Run<LedgerViewModel>(shell, "LEDG Cash apr");
        Assert.Equal("Cash, 01-04-2026 to 30-04-2026", april.Heading);
    }

    [Fact]
    public async Task Ledger_row_opens_the_voucher()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", Ledger(shell, "Cash"), Ledger(shell, "Sales"), 100);
        var s = await Run<LedgerViewModel>(shell, "LEDG Cash");
        await s.OpenAsync(s.Lines[1], newTab: false);
        Assert.Equal("RCPT RCT/26-27/00001", shell.Tabs.Active!.Command);
        Assert.IsType<JournalEntryViewModel>(shell.Tabs.Active.Screen);
        await shell.Tabs.BackAsync();
        Assert.IsType<LedgerViewModel>(shell.Tabs.Active!.Screen);
    }

    [Fact]
    public async Task Ledger_without_a_name_asks_for_one()
    {
        var shell = await Shell();
        var s = await Run<LedgerViewModel>(shell, "LEDG");
        Assert.Equal("Type a ledger name after LEDG.", s.Message);
        Assert.Empty(s.Lines);
        var none = await Run<LedgerViewModel>(shell, "LEDG no such ledger");
        Assert.Equal("No ledger named no such ledger.", none.Message);
    }

    [Fact]
    public async Task Trial_balance_drills_to_ledger_and_back()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", Ledger(shell, "Cash"), Ledger(shell, "Sales"), 100);
        var tb = await Run<TrialBalanceViewModel>(shell, "TRLB");
        await tb.OpenAsync(tb.Lines.Single(l => l.Cells[0].Text == "Cash"), newTab: false);
        Assert.Equal("LEDG Cash 01-04-2026 01-06-2026", shell.Tabs.Active!.Command);
        Assert.IsType<LedgerViewModel>(shell.Tabs.Active.Screen);
    }
}
