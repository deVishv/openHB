using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class PartiesTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<PartiesViewModel> Prty(ShellContext ctx, string input = "PRTY")
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var s = PartiesViewModel.Create(cmd, ctx);
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    static void Fill(PartiesViewModel s, string name)
    {
        s.FormName = name;
        s.FormGroup = s.Groups.Single(g => g.Kind == "TradeReceivables");
        s.FormState = s.States.Single(x => x.Code == "27");
        s.FormCreditDays = "30";
        s.FormAddress = "Pune";
    }

    [Fact]
    public async Task Party_gstin_sets_the_state()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Prty(shell.Context);
        s.FormState = s.States.Single(x => x.Code == "29");
        s.FormGstin = "27aaacr5055k1z7";
        Assert.Equal("27", s.FormState!.Code);
    }

    [Fact]
    public async Task Party_saves_with_opening_bills_and_shows_on_account()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Prty(shell.Context);
        Fill(s, "Ravi Stores");
        s.OpeningBills.Add(new OpeningBillLine { Number = "OB-1", Date = "15-03-2026", DueDate = "14-04-2026", Amount = "3,000" });
        await s.SaveAsync();
        Assert.Null(s.Message);
        await t.Eventually(() => s.Parties.Any(p => p.Name == "Ravi Stores"));
        var party = shell.Context.Lookups.Ledgers(Model.Lookups.LedgerUse.Party).Single();
        await t.OtherOwnerClient().Masters.SetOpeningAsync(new SetOpeningRequest { Company = shell.Context.Company!.Id, Ledger = party.Id, Paise = 5000_00 }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Id == party.Id).OpeningPaise == 5000_00);
        s.Selected = s.Parties.Single();
        await t.Eventually(() => s.OpeningBills.Count == 1);
        Assert.Equal(("OB-1", "3,000.00"), (s.OpeningBills[0].Number, s.OpeningBills[0].Amount));
        Assert.Equal("Opening balance 5,000.00 Dr; bills 3,000.00; on account 2,000.00 Dr", s.OpeningSummary);
    }

    [Fact]
    public async Task Party_opening_bill_errors_show_the_host_message()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Prty(shell.Context);
        Fill(s, "Ravi Stores");
        s.OpeningBills.Add(new OpeningBillLine { Number = "OB-1", Date = "01-04-2026", Amount = "100" });
        await s.SaveAsync();
        Assert.Equal("Line 1: an opening bill must be dated before 01-04-2026.", s.Message);
        Assert.Single(s.OpeningBills);
    }

    [Fact]
    public async Task Opening_bill_typing_errors_name_the_line()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Prty(shell.Context);
        Fill(s, "Ravi Stores");
        s.OpeningBills.Add(new OpeningBillLine { Number = "OB-1", Date = "15-03-2026", Amount = "100" });
        s.OpeningBills.Add(new OpeningBillLine { Number = "OB-2", Date = "31-02-2026", Amount = "100" });
        await s.SaveAsync();
        Assert.Equal("Line 2: enter the date as dd-mm-yyyy.", s.Message);
    }

    [Fact]
    public async Task Sub_screen_returns_the_new_party()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        int? saved = null;
        var s = PartiesViewModel.ForNew(shell.Context, "Blr Mart", PartySide.Receivable, id => { saved = id; return Task.CompletedTask; });
        await s.LoadAsync(CancellationToken.None);
        Assert.Equal(("Blr Mart", "TradeReceivables"), (s.FormName, s.FormGroup!.Kind));
        s.FormState = s.States.Single(x => x.Code == "29");
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.NotNull(saved);
        await t.Eventually(() => shell.Context.Lookups.Ledgers(Model.Lookups.LedgerUse.Party).Any(l => l.Id == saved));
    }

    [Fact]
    public async Task Sub_screen_escape_asks_before_discarding()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        var discarded = false;
        var s = PartiesViewModel.ForNew(shell.Context, "Blr Mart", PartySide.Payable, _ => Task.CompletedTask, () => discarded = true);
        await s.LoadAsync(CancellationToken.None);
        Assert.Equal("TradePayables", s.FormGroup!.Kind);
        Assert.True(s.Cancel());
        await t.Eventually(() => discarded);
        Assert.Equal("Discard this party?", confirm.LastQuestion);
    }
}
