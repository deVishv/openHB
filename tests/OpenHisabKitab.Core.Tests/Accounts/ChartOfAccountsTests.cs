namespace OpenHisabKitab.Core.Tests.Accounts;

public class ChartOfAccountsTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();

    [Fact]
    public void Every_required_ledger_exists() =>
        Assert.All(Enum.GetValues<RequiredLedger>(), r => Assert.True(c.Contains(c.Required(r))));

    [Fact]
    public void Every_tagged_group_kind_exists() =>
        Assert.All(Enum.GetValues<GroupKind>().Where(k => k != GroupKind.None),
            k => Assert.Contains(c.Groups, g => g.Id == c.Group(k)));

    [Theory]
    [InlineData(RequiredLedger.Cash, AccountNature.Asset)]
    [InlineData(RequiredLedger.Capital, AccountNature.Liability)]
    [InlineData(RequiredLedger.ProfitAndLoss, AccountNature.Liability)]
    [InlineData(RequiredLedger.Sales, AccountNature.Income)]
    [InlineData(RequiredLedger.Purchases, AccountNature.Expense)]
    [InlineData(RequiredLedger.RoundOff, AccountNature.Expense)]
    [InlineData(RequiredLedger.OutputCgst, AccountNature.Liability)]
    [InlineData(RequiredLedger.InputIgst, AccountNature.Asset)]
    public void Natures(RequiredLedger r, AccountNature n) => Assert.Equal(n, c.NatureOf(c.Required(r)));

    [Fact]
    public void Cash_or_bank()
    {
        var (c2, hdfc) = c.AddLedger("HDFC Bank", c.Group(GroupKind.BankAccounts));
        Assert.True(c2.IsCashOrBank(hdfc));
        Assert.True(c2.IsCashOrBank(c2.Required(RequiredLedger.Cash)));
        Assert.False(c2.IsCashOrBank(c2.Required(RequiredLedger.Sales)));
    }

    [Fact]
    public void Sub_group_inherits_nature_and_cash_kind()
    {
        var (c2, g) = c.AddGroup("Petty Cash", c.Group(GroupKind.CashInHand));
        var (c3, l) = c2.AddLedger("Office Petty Cash", g);
        Assert.Equal(AccountNature.Asset, c3.NatureOf(l));
        Assert.True(c3.IsCashOrBank(l));
    }

    [Theory]
    [InlineData("cash ")]
    [InlineData("CASH")]
    [InlineData("  ")]
    public void Rejects_duplicate_or_blank_ledger_names(string name) =>
        Assert.Throws<ChartException>(() => c.AddLedger(name, c.Group(GroupKind.CashInHand)));

    [Theory]
    [InlineData("Output  CGST")]
    [InlineData("output\tcgst")]
    [InlineData("Output\u00A0CGST")]
    public void Rejects_names_that_differ_only_in_spacing(string name) =>
        Assert.Throws<ChartException>(() => c.AddLedger(name, c.Group(GroupKind.DutiesAndTaxes)));

    [Fact]
    public void Rejects_duplicate_group_name() =>
        Assert.Throws<ChartException>(() => c.AddGroup("bank accounts", c.Group(GroupKind.CashInHand)));

    [Fact]
    public void Rejects_unknown_parent_group() =>
        Assert.Throws<ChartException>(() => c.AddLedger("Rent", new GroupId(9999)));

    [Fact]
    public void Add_returns_new_chart_and_leaves_original_unchanged()
    {
        var before = c.Ledgers.Count;
        var (c2, rent) = c.AddLedger("Rent", c.Group(GroupKind.OtherExpenses));
        Assert.Equal(before, c.Ledgers.Count);
        Assert.Equal(before + 1, c2.Ledgers.Count);
        Assert.False(c.Contains(rent));
        Assert.Equal("Rent", c2.Ledgers[^1].Name);
    }

    [Fact]
    public void Ids_are_sequential_from_one()
    {
        Assert.Equal(1, c.Groups[0].Id.Value);
        Assert.Equal(1, c.Ledgers[0].Id.Value);
        Assert.Equal(Enumerable.Range(1, c.Ledgers.Count), c.Ledgers.Select(l => l.Id.Value));
    }
}
