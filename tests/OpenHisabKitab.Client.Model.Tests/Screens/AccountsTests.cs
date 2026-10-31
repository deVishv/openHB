using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class AccountsTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<AccountsViewModel> Acct(ShellContext ctx, string input = "ACCT")
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var screen = (AccountsViewModel)AccountsViewModel.Create(cmd, ctx)!;
        await screen.LoadAsync(CancellationToken.None);
        return screen;
    }

    async Task<long> OpeningOf(ShellContext ctx, string ledger)
    {
        var chart = await t.OtherOwnerClient().Masters.GetChartAsync(new CompanyRef { Company = ctx.Company!.Id }, t.Headers());
        return chart.Ledgers.Single(l => l.Name == ledger).OpeningPaise;
    }

    static GroupMsg GroupOf(ShellContext ctx, string kind) => ctx.Lookups.Chart.Groups.Single(g => g.Kind == kind);

    [Fact]
    public async Task Shows_groups_and_ledgers_indented_with_openings()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        var cash = s.Rows.Single(r => !r.IsGroup && r.Required == "Required" && r.Name == "Cash");
        var cashGroup = s.Rows.Single(r => r.IsGroup && r.Id == shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == "Cash").Group);
        Assert.Equal(cashGroup.Depth + 1, cash.Depth);
        Assert.True(s.Rows.ToList().IndexOf(cashGroup) < s.Rows.ToList().IndexOf(cash));
        Assert.Equal("", cash.Opening);
        Assert.Equal("", s.OpeningDifference);
    }

    [Fact]
    public async Task Adds_a_ledger_with_a_credit_opening()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        await s.RunAsync("record.new");
        s.FormName = "HDFC Bank OD";
        s.FormParent.Set(GroupOf(shell.Context, "BankAccounts"));
        s.FormOpening = "5,000";
        s.FormSide = "Cr";
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.Equal(-500000, await OpeningOf(shell.Context, "HDFC Bank OD"));
        await t.Eventually(() => s.Rows.Any(r => r.Name == "HDFC Bank OD" && r.Opening == "5,000.00 Cr"));
    }

    [Fact]
    public async Task Shows_the_opening_difference_until_balanced()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        var cash = s.Rows.Single(r => r.Name == "Cash" && !r.IsGroup);
        s.Selected = cash;
        s.FormOpening = "1,000.00";
        s.FormSide = "Dr";
        await s.SaveAsync();
        await t.Eventually(() => s.OpeningDifference == "Opening difference 1,000.00 Dr");
        s.Selected = s.Rows.Single(r => r.Name == "Capital" && !r.IsGroup);
        s.FormOpening = "1000";
        s.FormSide = "Cr";
        await s.SaveAsync();
        await t.Eventually(() => s.OpeningDifference == "");
    }

    [Fact]
    public async Task Renames_a_ledger()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        s.Selected = s.Rows.Single(r => r.Name == "Cash" && !r.IsGroup);
        s.FormName = "Cash in Hand";
        await s.SaveAsync();
        Assert.Null(s.Message);
        await t.Eventually(() => s.Rows.Any(r => r.Name == "Cash in Hand"));
    }

    [Fact]
    public async Task Deletes_a_ledger_after_confirming()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        var s = await Acct(shell.Context);
        await s.RunAsync("record.new");
        s.FormName = "Petty Cash";
        s.FormParent.Set(GroupOf(shell.Context, "CashInHand"));
        await s.SaveAsync();
        await t.Eventually(() => s.Rows.Any(r => r.Name == "Petty Cash"));
        s.Selected = s.Rows.Single(r => r.Name == "Petty Cash");
        Assert.True(s.CanRun("record.delete"));
        await s.RunAsync("record.delete");
        Assert.Equal("Delete ledger Petty Cash?", confirm.LastQuestion);
        await t.Eventually(() => s.Rows.All(r => r.Name != "Petty Cash"));
    }

    [Fact]
    public async Task Groups_cannot_be_deleted()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        s.Selected = s.Rows.First(r => r.IsGroup);
        Assert.False(s.CanRun("record.delete"));
    }

    [Fact]
    public async Task A_bad_amount_says_how_to_write_it()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        s.Selected = s.Rows.Single(r => r.Name == "Cash" && !r.IsGroup);
        s.FormOpening = "12.345";
        await s.SaveAsync();
        Assert.Equal("Enter an amount like 12,345.60.", s.Message);
    }

    [Fact]
    public async Task Data_entry_cannot_edit_openings()
    {
        var owner = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(owner);
        var shell = await t.UserShellAsync("clerk", "DataEntry", company);
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = company, Name = "A Traders" });
        var s = await Acct(shell.Context);
        s.Selected = s.Rows.Single(r => r.Name == "Cash" && !r.IsGroup);
        Assert.False(s.CanEditOpenings);
    }

    [Fact]
    public async Task Income_ledgers_have_no_opening()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context);
        s.Selected = s.Rows.Single(r => r.Name == shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == "Sales").Name && !r.IsGroup);
        Assert.False(s.CanEditOpenings);
        s.Selected = s.Rows.Single(r => r.Name == "Cash" && !r.IsGroup);
        Assert.True(s.CanEditOpenings);
    }

    [Fact]
    public async Task Acct_with_a_name_selects_the_ledger()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Acct(shell.Context, "ACCT capital");
        Assert.Equal("Capital", s.Selected!.Name);
        Assert.Equal("Capital", s.FormName);
    }

    [Fact]
    public async Task Loads_with_no_host_calls()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var before = t.Requests.Count;
        await Acct(shell.Context);
        Assert.Equal(before, t.Requests.Count);
    }
}
