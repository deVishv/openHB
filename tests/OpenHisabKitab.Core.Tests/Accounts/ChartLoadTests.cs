namespace OpenHisabKitab.Core.Tests.Accounts;

public class ChartLoadTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();

    [Fact]
    public void Load_round_trips_default_chart()
    {
        var loaded = ChartOfAccounts.Load(c.Groups, c.Ledgers);
        Assert.Equal(c.Groups, loaded.Groups);
        Assert.Equal(c.Ledgers, loaded.Ledgers);
        Assert.Equal(c.Required(RequiredLedger.Cash), loaded.Required(RequiredLedger.Cash));
        Assert.Equal(c.Group(GroupKind.BankAccounts), loaded.Group(GroupKind.BankAccounts));
    }

    [Fact]
    public void Load_rejects_missing_required_ledger() =>
        Assert.Throws<ChartException>(() =>
            ChartOfAccounts.Load(c.Groups, c.Ledgers.Where(l => l.Required != RequiredLedger.RoundOff)));

    [Fact]
    public void Load_rejects_duplicate_required_ledger()
    {
        var cash = c.Ledger(c.Required(RequiredLedger.Cash));
        var extra = new LedgerAccount(new LedgerId(9999), "Second Cash", cash.Group, RequiredLedger.Cash);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load(c.Groups, [.. c.Ledgers, extra]));
    }

    [Fact]
    public void Load_rejects_duplicate_ledger_id()
    {
        var first = c.Ledgers[0];
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load(c.Groups, [.. c.Ledgers, first with { Name = "Other" }]));
    }

    [Fact]
    public void Load_rejects_duplicate_name_ignoring_case()
    {
        var first = c.Ledgers[0];
        var dup = new LedgerAccount(new LedgerId(9999), first.Name.ToUpperInvariant(), first.Group, null);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load(c.Groups, [.. c.Ledgers, dup]));
    }

    [Fact]
    public void Load_rejects_ledger_in_unknown_group()
    {
        var bad = new LedgerAccount(new LedgerId(9999), "Lost", new GroupId(9999), null);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load(c.Groups, [.. c.Ledgers, bad]));
    }

    [Fact]
    public void Load_rejects_unknown_parent()
    {
        var bad = new AccountGroup(new GroupId(9999), "Orphan", new GroupId(8888), AccountNature.Asset, GroupKind.None);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load([.. c.Groups, bad], c.Ledgers));
    }

    [Fact]
    public void Load_rejects_group_cycle()
    {
        var a = new AccountGroup(new GroupId(9998), "Loop A", new GroupId(9999), AccountNature.Asset, GroupKind.None);
        var b = new AccountGroup(new GroupId(9999), "Loop B", new GroupId(9998), AccountNature.Asset, GroupKind.None);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load([.. c.Groups, a, b], c.Ledgers));
    }

    [Fact]
    public void Load_rejects_child_with_other_nature()
    {
        var assetRoot = c.Groups.First(g => g.Parent is null && g.Nature == AccountNature.Asset);
        var bad = new AccountGroup(new GroupId(9999), "Odd", assetRoot.Id, AccountNature.Income, GroupKind.None);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load([.. c.Groups, bad], c.Ledgers));
    }

    [Fact]
    public void Load_rejects_duplicate_group_kind()
    {
        var bank = c.Ledger(c.Required(RequiredLedger.Cash)).Group;
        var bad = new AccountGroup(new GroupId(9999), "Second Banks", bank, AccountNature.Asset, GroupKind.BankAccounts);
        Assert.Throws<ChartException>(() => ChartOfAccounts.Load([.. c.Groups, bad], c.Ledgers));
    }

    [Fact]
    public void New_id_after_removal_does_not_collide()
    {
        var cashGroup = c.Group(GroupKind.CashInHand);
        var (c1, a) = c.AddLedger("A", cashGroup);
        var (c2, b) = c1.AddLedger("B", cashGroup);
        var (_, d) = c2.RemoveLedger(a).AddLedger("D", cashGroup);
        Assert.NotEqual(b, d);
    }

    [Fact]
    public void New_group_id_is_max_plus_one()
    {
        var max = c.Groups.Max(g => g.Id.Value);
        var (_, id) = c.AddGroup("Extra", c.Group(GroupKind.CashInHand));
        Assert.Equal(max + 1, id.Value);
    }

    [Fact]
    public void Removed_ledger_is_gone()
    {
        var (c1, a) = c.AddLedger("Temp", c.Group(GroupKind.CashInHand));
        Assert.False(c1.RemoveLedger(a).Contains(a));
    }

    [Fact]
    public void Required_ledger_cannot_be_removed()
    {
        var ex = Assert.Throws<ChartException>(() => c.RemoveLedger(c.Required(RequiredLedger.Cash)));
        Assert.Equal("Required ledger Cash cannot be deleted.", ex.Message);
    }

    [Fact]
    public void Rename_ledger_keeps_id_and_required()
    {
        var cash = c.Required(RequiredLedger.Cash);
        var renamed = c.RenameLedger(cash, "  Cash   in  Hand ");
        Assert.Equal("Cash in Hand", renamed.Ledger(cash).Name);
        Assert.Equal(cash, renamed.Required(RequiredLedger.Cash));
    }

    [Fact]
    public void Rename_ledger_to_its_own_name_in_other_case_works()
    {
        var cash = c.Required(RequiredLedger.Cash);
        Assert.Equal("CASH", c.RenameLedger(cash, "CASH").Ledger(cash).Name);
    }

    [Fact]
    public void Rename_to_existing_name_throws()
    {
        var (c1, a) = c.AddLedger("A", c.Group(GroupKind.CashInHand));
        Assert.Throws<ChartException>(() => c1.RenameLedger(a, "cash"));
    }

    [Fact]
    public void Rename_group()
    {
        var g = c.Group(GroupKind.BankAccounts);
        Assert.Equal("Banks", c.RenameGroup(g, "Banks").Groups.Single(x => x.Id == g).Name);
    }

    [Fact]
    public void Rename_group_blank_throws() =>
        Assert.Throws<ChartException>(() => c.RenameGroup(c.Group(GroupKind.BankAccounts), "   "));
}
