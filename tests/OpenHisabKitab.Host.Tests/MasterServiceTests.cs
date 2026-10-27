namespace OpenHisabKitab.Host.Tests;

public sealed class MasterServiceTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;

    public MasterServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
    }

    public void Dispose() => host.Dispose();

    static ServiceException Fails(ServiceError error, Action act)
    {
        var ex = Assert.Throws<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    ChartOfAccounts Chart => host.Masters.Chart(owner, company);

    long AuditCount()
    {
        using var c = host.Hub.Open(company);
        return AuditLog.Verify(c).Count;
    }

    [Fact]
    public void Add_ledger_and_group()
    {
        var g = host.Masters.AddGroup(owner, company, "Branch Cash", Chart.Group(GroupKind.CashInHand));
        var l = host.Masters.AddLedger(owner, company, "Pune Branch Cash", g);
        Assert.Equal("Pune Branch Cash", Chart.Ledger(l).Name);
        Assert.True(Chart.IsCashOrBank(l));
    }

    [Fact]
    public void Every_write_adds_one_audit_row_one_event_and_tells_the_books()
    {
        var events = new List<ChangeEvent>();
        using var cts = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            await foreach (var e in host.Feed.Subscribe(cts.Token)) events.Add(e);
        });
        SpinWait.SpinUntil(() => host.Feed.SubscriberCount == 1, TimeSpan.FromSeconds(5));

        var before = AuditCount();
        var l = host.Masters.AddLedger(owner, company, "HDFC Bank", Chart.Group(GroupKind.BankAccounts));
        host.Masters.Rename(owner, company, l, "HDFC Bank 0042");
        host.Masters.SetOpening(owner, company, l, Money.FromRupees(100));
        Assert.Equal(before + 3, AuditCount());
        SpinWait.SpinUntil(() => events.Count == 3, TimeSpan.FromSeconds(5));
        Assert.All(events, e => Assert.Equal(new ChangeEvent(company, ChangeKind.Masters, l.Value), e));
        Assert.Equal(3, host.Observer.MastersChanged.Count(x => x == company));
        cts.Cancel();
    }

    [Fact]
    public void Renaming_a_required_ledger_works()
    {
        var cash = Chart.Required(RequiredLedger.Cash);
        host.Masters.Rename(owner, company, cash, "Cash in Hand");
        Assert.Equal("Cash in Hand", Chart.Ledger(cash).Name);
    }

    [Fact]
    public void Chart_errors_become_invalid()
    {
        var ex = Fails(ServiceError.Invalid, () => host.Masters.AddLedger(owner, company, "cash", Chart.Group(GroupKind.CashInHand)));
        Assert.Equal("A ledger named cash already exists.", ex.Message);
        Fails(ServiceError.Invalid, () => host.Masters.RemoveLedger(owner, company, Chart.Required(RequiredLedger.Cash)));
    }

    [Fact]
    public void Ledger_with_opening_balance_cannot_be_deleted()
    {
        var l = host.Masters.AddLedger(owner, company, "Old Loan", Chart.Group(GroupKind.CashInHand));
        host.Masters.SetOpening(owner, company, l, Money.FromRupees(5));
        var ex = Fails(ServiceError.Invalid, () => host.Masters.RemoveLedger(owner, company, l));
        Assert.Equal("Ledger Old Loan has entries and cannot be deleted.", ex.Message);
    }

    [Fact]
    public void Ledger_with_postings_cannot_be_deleted()
    {
        var l = host.Masters.AddLedger(owner, company, "Petty Cash", Chart.Group(GroupKind.CashInHand));
        host.Hub.Write(company, tx => VoucherRepository.Insert(tx, new VoucherRow(0, VoucherType.Contra, "Main", 2026, 1,
            "CON/26-27/00001", new DateOnly(2026, 5, 1), "", false, 1, 0,
            [new PostingLine(l, Money.FromRupees(5)), new PostingLine(Chart.Required(RequiredLedger.Cash), Money.FromRupees(-5))], null)));
        var ex = Fails(ServiceError.Invalid, () => host.Masters.RemoveLedger(owner, company, l));
        Assert.Equal("Ledger Petty Cash has entries and cannot be deleted.", ex.Message);
    }

    [Fact]
    public void Unused_ledger_can_be_deleted()
    {
        var l = host.Masters.AddLedger(owner, company, "Spare", Chart.Group(GroupKind.CashInHand));
        host.Masters.RemoveLedger(owner, company, l);
        Assert.False(Chart.Contains(l));
    }

    [Fact]
    public void Opening_cannot_change_once_books_start_is_locked()
    {
        host.Hub.Write(company, tx => { LockRepository.Set(tx, new DateOnly(2026, 4, 1)); return 0; });
        var ex = Fails(ServiceError.Locked, () =>
            host.Masters.SetOpening(owner, company, Chart.Required(RequiredLedger.Cash), Money.FromRupees(1)));
        Assert.Equal("Books are locked up to 01-04-2026.", ex.Message);
    }

    [Fact]
    public void Party_round_trips()
    {
        var p = new Party(default, TestHost.G("29AAGCB7383J1Z4"), new StateCode("29"), 30, "Bengaluru", 0);
        var l = host.Masters.SaveParty(owner, company, "Ravi Stores", Chart.Group(GroupKind.TradeReceivables), p);
        var stored = host.Masters.Parties(owner, company).Single();
        Assert.Equal(p with { Ledger = l, RowVersion = 1 }, stored);
        Assert.Equal("Ravi Stores", Chart.Ledger(l).Name);

        host.Masters.SaveParty(owner, company, "Ravi Stores LLP", Chart.Group(GroupKind.TradeReceivables), stored with { CreditDays = 45 });
        Assert.Equal(45, host.Masters.Parties(owner, company).Single().CreditDays);
        Assert.Equal("Ravi Stores LLP", Chart.Ledger(l).Name);
    }

    [Fact]
    public void Stale_party_edit_is_a_conflict()
    {
        var p = new Party(default, null, new StateCode("27"), 0, "", 0);
        var l = host.Masters.SaveParty(owner, company, "Walk In", Chart.Group(GroupKind.TradeReceivables), p);
        var stored = host.Masters.Parties(owner, company).Single();
        host.Masters.SaveParty(owner, company, "Walk In", Chart.Group(GroupKind.TradeReceivables), stored with { CreditDays = 7 });
        var ex = Fails(ServiceError.Conflict, () =>
            host.Masters.SaveParty(owner, company, "Walk In", Chart.Group(GroupKind.TradeReceivables), stored with { CreditDays = 9 }));
        Assert.Equal(7, ((Party)ex.Current!).CreditDays);
        Assert.Equal(l, ((Party)ex.Current!).Ledger);
    }

    [Fact]
    public void Party_under_sales_is_refused()
    {
        var p = new Party(default, null, new StateCode("27"), 0, "", 0);
        var ex = Fails(ServiceError.Invalid, () =>
            host.Masters.SaveParty(owner, company, "Odd", Chart.Group(GroupKind.RevenueFromOperations), p));
        Assert.Equal("Parties belong under Trade Receivables or Trade Payables.", ex.Message);
    }

    [Fact]
    public void Party_under_a_subgroup_of_payables_is_fine()
    {
        var sub = host.Masters.AddGroup(owner, company, "Steel Suppliers", Chart.Group(GroupKind.TradePayables));
        host.Masters.SaveParty(owner, company, "Tata Steel", sub, new Party(default, null, new StateCode("20"), 0, "", 0));
    }

    [Fact]
    public void Party_gstin_state_must_match()
    {
        var p = new Party(default, TestHost.G("29AAGCB7383J1Z4"), new StateCode("27"), 0, "", 0);
        var ex = Fails(ServiceError.Invalid, () =>
            host.Masters.SaveParty(owner, company, "Ravi", Chart.Group(GroupKind.TradeReceivables), p));
        Assert.Equal("GSTIN state does not match the party state.", ex.Message);
    }

    [Fact]
    public void Item_rules()
    {
        var item = new Item(0, "Steel Rod", "7214", false, "KG", new GstRate(1800), new GstRate(0), Money.Parse("65.50"), 0);
        var id = host.Masters.SaveItem(owner, company, item);
        Assert.Equal(item with { Id = id, RowVersion = 1 }, host.Masters.Items(owner, company).Single());

        Assert.Equal("HSN or SAC must be 4, 6, or 8 digits.",
            Fails(ServiceError.Invalid, () => host.Masters.SaveItem(owner, company, item with { Name = "A", HsnSac = "72145" })).Message);
        Assert.Equal("GST rate 7% is not valid.",
            Fails(ServiceError.Invalid, () => host.Masters.SaveItem(owner, company, item with { Name = "B", Rate = new GstRate(700) })).Message);
        Assert.Equal("Item steel rod already exists.",
            Fails(ServiceError.Invalid, () => host.Masters.SaveItem(owner, company, item with { Name = "steel rod" })).Message);
        var diamond = host.Masters.SaveItem(owner, company, item with { Name = "Diamond", Rate = new GstRate(25) });
        Assert.Equal(25, host.Masters.Items(owner, company).Single(x => x.Id == diamond).Rate.BasisPoints);
    }

    [Fact]
    public void Viewer_cannot_edit_masters_but_can_read_them()
    {
        var viewer = host.NewUser("ravi", BuiltInRole.Viewer, company);
        Fails(ServiceError.PermissionDenied, () => host.Masters.AddLedger(viewer.Context, company, "X", Chart.Group(GroupKind.CashInHand)));
        Assert.NotEmpty(host.Masters.Chart(viewer.Context, company).Ledgers);
    }

    [Fact]
    public void Income_and_expense_ledgers_take_no_opening_balance()
    {
        var ex = Fails(ServiceError.Invalid, () =>
            host.Masters.SetOpening(owner, company, Chart.Required(RequiredLedger.Sales), Money.FromRupees(10)));
        Assert.Equal("Income and expense ledgers have no opening balance.", ex.Message);
        host.Masters.SetOpening(owner, company, Chart.Required(RequiredLedger.Sales), Money.Zero);
        Assert.NotNull(host.Books.Get(company));
    }
}
