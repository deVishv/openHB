namespace OpenHisabKitab.Host.Tests;

public sealed class OpeningBillsTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId party;

    public OpeningBillsTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        party = host.Masters.SavePartyAsync(owner, company, "Ravi Stores", chart.Group(GroupKind.TradeReceivables),
            new Party(default, null, new StateCode("27"), 30, "", 0)).GetAwaiter().GetResult();
    }

    public void Dispose() => host.Dispose();

    static OpeningBillRow Bill(string number, string amount, string date = "2026-03-15", string? due = null, long id = 0) =>
        new(id, default, number, DateOnly.Parse(date), due is null ? null : DateOnly.Parse(due), Money.Parse(amount));

    Task<long> Set(params OpeningBillRow[] bills) =>
        host.Masters.SetOpeningBillsAsync(owner, company, party, host.Masters.OpeningBills(owner, company, party).Version, bills);

    async Task<string> Refused(ServiceError error, Func<Task> act)
    {
        var e = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, e.Error);
        return e.Message;
    }

    long AuditCount()
    {
        using var c = host.Hub.Open(company);
        return AuditLog.Verify(c).Count;
    }

    [Fact]
    public async Task Saved_bills_read_back_with_a_new_version_and_audit()
    {
        var before = AuditCount();
        var version = await Set(Bill("OB-1", "2,000.00", due: "2026-04-15"), Bill("OB-2", "500.50"));
        var read = host.Masters.OpeningBills(owner, company, party);
        Assert.Equal(version, read.Version);
        Assert.Equal(2, version);
        Assert.Equal(["OB-1", "OB-2"], read.Bills.Select(b => b.Number));
        Assert.All(read.Bills, b => Assert.Equal(party, b.Party));
        Assert.Equal(before + 1, AuditCount());
        using var c = host.Hub.Open(company);
        var last = AuditLog.Read(c, new AuditFilter(null, null, null, null, 1)).Single();
        Assert.Equal(("set-opening-bills", "party", party.Value.ToString(CultureInfo.InvariantCulture)), (last.Entry.Action, last.Entry.Entity, last.Entry.EntityKey));
    }

    [Fact]
    public async Task Party_row_version_does_not_change()
    {
        var rv = host.Masters.Parties(owner, company).Single().RowVersion;
        await Set(Bill("OB-1", "100.00"));
        Assert.Equal(rv, host.Masters.Parties(owner, company).Single().RowVersion);
    }

    [Fact]
    public async Task Not_a_party_is_not_found() =>
        Assert.Equal("No such party.", await Refused(ServiceError.NotFound,
            () => host.Masters.SetOpeningBillsAsync(owner, company, host.Masters.Chart(owner, company).Required(RequiredLedger.Cash), 1, [])));

    [Fact]
    public async Task Stale_version_is_a_conflict_with_the_current_bills()
    {
        await Set(Bill("OB-1", "100.00"));
        var e = await Assert.ThrowsAsync<ServiceException>(() => host.Masters.SetOpeningBillsAsync(owner, company, party, 1, []));
        Assert.Equal((ServiceError.Conflict, "Someone else changed these opening bills. Showing the current version."), (e.Error, e.Message));
        Assert.Equal("OB-1", Assert.IsType<OpeningBills>(e.Current).Bills.Single().Number);
    }

    [Fact]
    public async Task Locked_books_refuse()
    {
        await host.Admin.LockUpToAsync(owner, company, new DateOnly(2026, 4, 30));
        Assert.Equal("Books are locked up to 30-04-2026.", await Refused(ServiceError.Locked, () => Set(Bill("OB-1", "100.00"))));
    }

    [Theory]
    [InlineData("  ", "100.00", "2026-03-15", null, "Line 1: enter the bill number.")]
    [InlineData("OB-1", "100.00", "2026-04-01", null, "Line 1: an opening bill must be dated before 01-04-2026.")]
    [InlineData("OB-1", "100.00", "2026-03-15", "2026-03-14", "Line 1: the due date is before the bill date.")]
    [InlineData("OB-1", "0.00", "2026-03-15", null, "Line 1: enter an amount more than zero.")]
    [InlineData("OB-1", "-5.00", "2026-03-15", null, "Line 1: enter an amount more than zero.")]
    public async Task Bad_lines_are_refused(string number, string amount, string date, string? due, string message) =>
        Assert.Equal(message, await Refused(ServiceError.Invalid, () => Set(Bill(number, amount, date, due))));

    [Fact]
    public async Task A_repeated_number_is_refused_ignoring_case() =>
        Assert.Equal("Line 2: bill ob-1 is listed twice.",
            await Refused(ServiceError.Invalid, () => Set(Bill("OB-1", "100.00"), Bill("ob-1", "200.00"))));

    [Fact]
    public async Task An_unknown_id_is_refused() =>
        Assert.Equal("Line 1: unknown bill.", await Refused(ServiceError.Invalid, () => Set(Bill("OB-1", "100.00", id: 999))));

    [Fact]
    public async Task Numbers_are_trimmed()
    {
        await Set(Bill("  OB-1 ", "100.00"));
        Assert.Equal("OB-1", host.Masters.OpeningBills(owner, company, party).Bills.Single().Number);
    }

    [Fact]
    public async Task Data_entry_cannot_set_opening_bills()
    {
        var clerk = host.NewUser("clerk", BuiltInRole.DataEntry, company).Context;
        await Refused(ServiceError.PermissionDenied, () => host.Masters.SetOpeningBillsAsync(clerk, company, party, 1, [Bill("OB-1", "100.00")]));
    }

    [Fact]
    public async Task Removing_a_settled_opening_bill_is_refused()
    {
        await Set(Bill("OB-1", "500.00"));
        var stored = host.Masters.OpeningBills(owner, company, party).Bills.Single();
        var cash = host.Masters.Chart(owner, company).Required(RequiredLedger.Cash);
        await host.Vouchers.SaveJournalAsync(owner, company, new JournalInput(VoucherType.Receipt, new DateOnly(2026, 5, 1), "",
            [new PostingLine(cash, Money.Parse("200.00")), new PostingLine(party, Money.Parse("-200.00"))],
            [new AllocationRow(party, new BillRef(BillSource.Opening, stored.Id), Money.Parse("200.00"))]));
        Assert.Equal("Bill OB-1 has 200.00 settled. Change those vouchers first.", await Refused(ServiceError.Invalid, () => Set()));
        Assert.Equal("Bill OB-1 has 200.00 settled. Change those vouchers first.",
            await Refused(ServiceError.Invalid, () => Set(stored with { Amount = Money.Parse("199.99") })));
        await Set(stored with { Amount = Money.Parse("200.00") });
    }
    [Fact]
    public async Task A_party_with_opening_bills_cannot_be_deleted()
    {
        await Set(Bill("OB-1", "100.00"));
        Assert.Equal("Party Ravi Stores has opening bills. Remove them in PRTY first.",
            await Refused(ServiceError.Invalid, () => host.Masters.RemoveLedgerAsync(owner, company, party)));
    }
}
