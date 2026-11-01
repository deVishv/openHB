using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class FindTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    async Task<int> Party(ShellViewModel shell, string name)
    {
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var id = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = shell.Context.Company!.Id, Name = name, Group = group, Party = new PartyMsg { State = "27" },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(id));
        return id;
    }

    async Task Sale(ShellViewModel shell, int party, long paise)
    {
        var sales = shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == "Sales").Id;
        await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = shell.Context.Company!.Id,
            Invoice = new InvoiceMsg
            {
                Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
                Lines = { new InvoiceLineMsg { Ledger = sales, Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = paise } },
            },
        }, t.Headers());
    }

    async Task Receipts(ShellViewModel shell, int count, string narration)
    {
        var chart = shell.Context.Lookups.Chart;
        var (cash, sales) = (chart.Ledgers.Single(l => l.Required == "Cash").Id, chart.Ledgers.Single(l => l.Required == "Sales").Id);
        for (var i = 0; i < count; i++)
            await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
            {
                Company = shell.Context.Company!.Id,
                Journal = new JournalMsg
                {
                    Type = "Receipt", Date = "2026-05-10", Narration = narration,
                    Lines = { new PostingLineMsg { Ledger = cash, Paise = 100 }, new PostingLineMsg { Ledger = sales, Paise = -100 } },
                },
            }, t.Headers());
    }

    [Fact]
    public async Task Find_by_party_name_lists_its_vouchers()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores");
        await Sale(shell, party, 1000_00);
        await Receipts(shell, 1, "other");
        await shell.RunAsync("FIND ravi", newTab: true);
        var s = Assert.IsType<FindViewModel>(shell.Tabs.Active!.Screen);
        var row = Assert.Single(s.Rows);
        Assert.Equal(("SALE", "INV/26-27/00001", "Ravi Stores", "1,000.00", "01-06-2026"), (row.Code, row.Number, row.Party, row.Amount, row.Date));
        Assert.Equal("ravi", s.Subject);
    }

    [Fact]
    public async Task More_loads_the_next_page()
    {
        var shell = await Shell();
        await Receipts(shell, 105, "rent");
        await shell.RunAsync("FIND rent", newTab: true);
        var s = Assert.IsType<FindViewModel>(shell.Tabs.Active!.Screen);
        Assert.Equal(100, s.Rows.Count);
        Assert.True(s.HasMore);
        await s.MoreAsync();
        Assert.Equal(105, s.Rows.Count);
        Assert.False(s.HasMore);
    }

    [Fact]
    public async Task Enter_opens_the_voucher_in_its_entry_screen()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores");
        await Sale(shell, party, 1000_00);
        await shell.RunAsync("FIND ravi", newTab: true);
        var s = (FindViewModel)shell.Tabs.Active!.Screen;
        await s.OpenAsync(s.Rows[0], newTab: false);
        Assert.Equal("SALE INV/26-27/00001", shell.Tabs.Active!.Command);
        var sale = Assert.IsType<InvoiceEntryViewModel>(shell.Tabs.Active.Screen);
        Assert.True(sale.IsEditing);
        await shell.Tabs.BackAsync();
        Assert.IsType<FindViewModel>(shell.Tabs.Active!.Screen);
    }

    [Fact]
    public async Task No_results_says_so()
    {
        var shell = await Shell();
        await shell.RunAsync("FIND nothing", newTab: true);
        var s = (FindViewModel)shell.Tabs.Active!.Screen;
        Assert.Empty(s.Rows);
        Assert.Equal("No vouchers match nothing.", s.Message);
    }

    [Fact]
    public async Task Data_entry_sees_only_their_own()
    {
        var owner = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(owner);
        var chart = await owner.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o));
        var (cash, sales) = (chart.Ledgers.Single(l => l.Required == "Cash").Id, chart.Ledgers.Single(l => l.Required == "Sales").Id);
        JournalRequest Rent() => new()
        {
            Company = company,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10", Narration = "rent",
                Lines = { new PostingLineMsg { Ledger = cash, Paise = 100 }, new PostingLineMsg { Ledger = sales, Paise = -100 } },
            },
        };
        await owner.CallAsync((h, o) => h.Vouchers.SaveJournalAsync(Rent(), o));
        var clerk = await t.UserShellAsync("clerk", "DataEntry", company);
        await CompanyOpener.OpenAsync(clerk.Context, new CompanyEntry { Id = company, Name = "A Traders" });
        await clerk.Context.Host.CallAsync((h, o) => h.Vouchers.SaveJournalAsync(Rent(), o));
        Assert.True(Core.Commands.CommandParser.TryParse("FIND rent", out var cmd));
        var s = (FindViewModel)(await FindViewModel.CreateAsync(cmd, clerk.Context))!;
        Assert.Single(s.Rows);
        Assert.Equal("RCT/26-27/00002", s.Rows[0].Number);
    }
}
