using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class CashFlowAndOutstandingTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Ledger(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task Receipt(ShellViewModel shell, string cr, long paise)
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10", Narration = "in",
                Lines =
                {
                    new PostingLineMsg { Ledger = Ledger(shell, "Cash"), Paise = paise },
                    new PostingLineMsg { Ledger = Ledger(shell, cr), Paise = -paise },
                },
            },
        }, t.Headers());
    }

    async Task SaleToRavi(ShellViewModel shell, long paise)
    {
        var company = shell.Context.Company!.Id;
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var party = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = company, Name = "Ravi Stores", Group = group, Party = new PartyMsg { State = "27" },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(party));
        await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = company,
            Invoice = new InvoiceMsg
            {
                Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
                Lines = { new InvoiceLineMsg { Ledger = Ledger(shell, "Sales"), Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = paise } },
            },
        }, t.Headers());
    }

    static async Task<T> Run<T>(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        return Assert.IsType<T>(shell.Tabs.Active!.Screen);
    }

    static IReadOnlyList<string> Texts(ReportLine line) => [.. line.Cells.Select(c => c.Text)];

    [Fact]
    public async Task Cash_flow_sections_and_closing_cash()
    {
        var shell = await Shell();
        await Receipt(shell, "Sales", 100000);
        var s = await Run<CashFlowViewModel>(shell, "CFLO");
        Assert.Equal("Cash flow, 01-04-2026 to 01-06-2026", s.Heading);
        Assert.Equal(
            [
                "Cash from operating activities", "Net profit", "Total", "Cash from investing activities", "Total",
                "Cash from financing activities", "Total", "Net change in cash", "Opening cash and cash equivalents",
                "Closing cash and cash equivalents",
            ],
            s.Lines.Select(l => l.Cells[0].Text));
        Assert.Equal(["Net profit", "1,000.00"], Texts(s.Lines[1]));
        Assert.Equal("PNLS 01-04-2026 01-06-2026", s.Lines[1].Drill);
        Assert.Equal(["Closing cash and cash equivalents", "1,000.00"], Texts(s.Lines[^1]));
        Assert.Equal("TRLB Current Assets 01-04-2026 01-06-2026", s.Lines[^1].Drill);
    }

    [Fact]
    public async Task Cash_flow_line_drills_to_its_group()
    {
        var shell = await Shell();
        await Receipt(shell, "Capital", 50000);
        var s = await Run<CashFlowViewModel>(shell, "CFLO fy");
        var capital = s.Lines.Single(l => l.Cells[0].Text == "Change in Capital");
        Assert.Equal("500.00", capital.Cells[1].Text);
        Assert.Equal("TRLB Capital 01-04-2026 31-03-2027", capital.Drill);
    }

    [Fact]
    public async Task Outstanding_reads_side_and_date()
    {
        var shell = await Shell();
        var s = await Run<OutstandingViewModel>(shell, "OUTS payable 31-07-2026");
        Assert.Equal("Payables as at 31-07-2026", s.Heading);
        var r = await Run<OutstandingViewModel>(shell, "OUTS");
        Assert.Equal("Receivables as at 01-06-2026", r.Heading);
    }

    [Fact]
    public async Task Outstanding_party_drills_to_its_bills_and_a_bill_to_its_voucher()
    {
        var shell = await Shell();
        await SaleToRavi(shell, 70000);
        var s = await Run<OutstandingViewModel>(shell, "OUTS 31-07-2026");
        Assert.Equal(["Party", "Not due", "1-30", "31-60", "61-90", "Over 90", "On account", "Total"], s.Columns.Select(c => c.Header));
        var ravi = s.Lines[0];
        Assert.Equal(["Ravi Stores", "0.00", "0.00", "700.00", "0.00", "0.00", "0.00", "700.00"], Texts(ravi));
        Assert.Equal(["Total", "0.00", "0.00", "700.00", "0.00", "0.00", "0.00", "700.00"], Texts(s.Lines[^1]));
        Assert.Equal("OUTS receivable Ravi Stores 31-07-2026", ravi.Drill);

        await s.OpenAsync(ravi, newTab: false);
        var bills = Assert.IsType<OutstandingViewModel>(shell.Tabs.Active!.Screen);
        Assert.Equal("Receivables, Ravi Stores, as at 31-07-2026", bills.Heading);
        Assert.Equal(["Bill", "Party document", "Date", "Due", "Days overdue", "Amount", "Open"], bills.Columns.Select(c => c.Header));
        Assert.Equal(["INV/26-27/00001", "", "01-06-2026", "01-06-2026", "60", "700.00", "700.00"], Texts(bills.Lines[0]));
        Assert.Equal("SALE INV/26-27/00001", bills.Lines[0].Drill);

        await bills.OpenAsync(bills.Lines[0], newTab: false);
        Assert.IsType<InvoiceEntryViewModel>(shell.Tabs.Active!.Screen);
    }

    [Fact]
    public async Task Bucket_headings_follow_the_setting()
    {
        var shell = await Shell();
        await SaleToRavi(shell, 100);
        await t.OtherOwnerClient().Companies.SetSettingAsync(new SettingRequest
        {
            Company = shell.Context.Company!.Id, Key = "outstanding.ageingDays", Json = "\"15,45\"",
        }, t.Headers());
        var s = await Run<OutstandingViewModel>(shell, "OUTS 31-07-2026");
        Assert.Equal(["Party", "Not due", "1-15", "16-45", "Over 45", "On account", "Total"], s.Columns.Select(c => c.Header));
    }
}
