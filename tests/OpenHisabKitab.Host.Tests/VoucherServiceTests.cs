namespace OpenHisabKitab.Host.Tests;

public sealed class VoucherServiceTests : IDisposable
{
    readonly TestHost host = new();
    readonly int company;
    readonly CallContext owner;
    readonly LedgerId cash, sales, bank;

    public VoucherServiceTests()
    {
        company = host.NewCompany();
        owner = host.Owner.Context;
        var chart = host.Masters.Chart(owner, company);
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
        bank = host.Masters.AddLedger(owner, company, "HDFC Bank", chart.Group(GroupKind.BankAccounts));
    }

    public void Dispose() => host.Dispose();

    static async Task<ServiceException> Fails(ServiceError error, Func<Task> act)
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(act);
        Assert.Equal(error, ex.Error);
        return ex;
    }

    JournalInput Receipt(DateOnly date, long rupees) =>
        new(VoucherType.Receipt, date, "cash sale",
            [new PostingLine(cash, Money.FromRupees(rupees)), new PostingLine(sales, Money.FromRupees(-rupees))]);

    static readonly DateOnly May10 = new(2026, 5, 10);

    [Fact]
    public async Task Receipts_are_numbered_in_order()
    {
        var first = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 500));
        var second = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 700));
        Assert.Equal("RCT/26-27/00001", first.DisplayNumber);
        Assert.Equal("RCT/26-27/00002", second.DisplayNumber);
        Assert.Equal(1, first.RowVersion);
        var stored = host.Vouchers.Get(owner, company, first.Id);
        Assert.Equal(May10, stored.Date);
        Assert.Equal(host.Owner.Context.UserId, stored.CreatedBy);
        Assert.Equal([new PostingRecord(first.Id, May10, cash, Money.FromRupees(500)), new PostingRecord(first.Id, May10, sales, Money.FromRupees(-500))],
            host.Observer.Saved.Single(s => s.Voucher == first.Id).Postings);
    }

    [Fact]
    public async Task Unbalanced_journal_is_refused()
    {
        var input = new JournalInput(VoucherType.Journal, May10, "",
            [new PostingLine(cash, Money.FromRupees(5)), new PostingLine(sales, Money.FromRupees(-4))]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, input));
        Assert.Equal("Debits and credits differ.", ex.Message);
    }

    [Fact]
    public async Task Typed_zero_line_is_refused_even_though_it_would_consolidate_away()
    {
        var input = new JournalInput(VoucherType.Journal, May10, "",
            [new PostingLine(cash, Money.FromRupees(5)), new PostingLine(bank, Money.Zero), new PostingLine(sales, Money.FromRupees(-5))]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, input));
        Assert.Equal("Remove lines with a zero amount.", ex.Message);
    }

    [Fact]
    public async Task Lines_that_cancel_out_leave_too_few_lines()
    {
        var input = new JournalInput(VoucherType.Journal, May10, "",
            [new PostingLine(cash, Money.FromRupees(5)), new PostingLine(cash, Money.FromRupees(-5))]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, input));
        Assert.Equal("Enter at least two lines.", ex.Message);
    }

    [Fact]
    public async Task Contra_needs_cash_or_bank()
    {
        var input = new JournalInput(VoucherType.Contra, May10, "",
            [new PostingLine(cash, Money.FromRupees(5)), new PostingLine(sales, Money.FromRupees(-5))]);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, input));
        Assert.Equal("Contra uses only cash and bank ledgers.", ex.Message);
    }

    [Fact]
    public async Task Invoice_types_are_sent_to_invoice_entry()
    {
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5) with { Type = VoucherType.Sale }));
        Assert.Equal("Use the invoice entry for Sale.", ex.Message);
    }

    [Fact]
    public async Task Date_outside_any_year_is_refused()
    {
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2027, 4, 1), 5)));
        Assert.Equal("No financial year for 01-04-2027. Add it in CMPY.", ex.Message);
        await Fails(ServiceError.Invalid, () => host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2026, 3, 31), 5)));
    }

    [Fact]
    public async Task Lock_date_is_refused_and_the_day_after_accepted()
    {
        host.Hub.Write(company, tx => { LockRepository.Set(tx, new DateOnly(2026, 6, 30)); return 0; });
        var ex = await Fails(ServiceError.Locked, () => host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2026, 6, 30), 5)));
        Assert.Equal("Books are locked up to 30-06-2026.", ex.Message);
        await host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2026, 7, 1), 5));
    }

    [Fact]
    public async Task Edit_cannot_leave_the_financial_year()
    {
        host.Companies.AddYear(owner, company);
        var saved = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        var ex = await Fails(ServiceError.Invalid, () =>
            host.Vouchers.EditJournalAsync(owner, company, saved.Id, saved.RowVersion, Receipt(new DateOnly(2027, 4, 10), 5)));
        Assert.Equal("Date must stay in FY 2026-27.", ex.Message);
    }

    [Fact]
    public async Task Edit_into_or_out_of_a_locked_period_is_refused()
    {
        var early = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2026, 5, 1), 5));
        var late = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(new DateOnly(2026, 8, 1), 5));
        host.Hub.Write(company, tx => { LockRepository.Set(tx, new DateOnly(2026, 6, 30)); return 0; });
        await Fails(ServiceError.Locked, () => host.Vouchers.EditJournalAsync(owner, company, late.Id, late.RowVersion, Receipt(new DateOnly(2026, 6, 1), 5)));
        await Fails(ServiceError.Locked, () => host.Vouchers.EditJournalAsync(owner, company, early.Id, early.RowVersion, Receipt(new DateOnly(2026, 8, 1), 5)));
        await Fails(ServiceError.Locked, () => host.Vouchers.CancelAsync(owner, company, early.Id, early.RowVersion, "wrong"));
    }

    [Fact]
    public async Task Opening_difference_blocks_saving()
    {
        host.Masters.SetOpening(owner, company, cash, Money.FromRupees(100));
        var ex = await Fails(ServiceError.OpeningDifference, () => host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5)));
        Assert.Equal("Opening balances differ by 100.00. Fix them in ACCT.", ex.Message);
    }

    [Fact]
    public async Task Edit_replaces_lines_and_bumps_row_version()
    {
        var saved = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        var edited = await host.Vouchers.EditJournalAsync(owner, company, saved.Id, saved.RowVersion, Receipt(new DateOnly(2026, 5, 11), 9));
        Assert.Equal(saved.DisplayNumber, edited.DisplayNumber);
        Assert.Equal(2, edited.RowVersion);
        var v = host.Vouchers.Get(owner, company, saved.Id);
        Assert.Equal(Money.FromRupees(9), v.Postings[0].Amount);
        Assert.Equal(Money.FromRupees(9), host.Observer.Saved.Last().Postings[0].Amount);
    }

    [Fact]
    public async Task Stale_edit_is_a_conflict_carrying_the_current_voucher()
    {
        var saved = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        await host.Vouchers.EditJournalAsync(owner, company, saved.Id, saved.RowVersion, Receipt(May10, 6));
        var ex = await Fails(ServiceError.Conflict, () => host.Vouchers.EditJournalAsync(owner, company, saved.Id, saved.RowVersion, Receipt(May10, 7)));
        Assert.Equal("Someone else changed this voucher. Showing the current version.", ex.Message);
        var current = Assert.IsType<VoucherRow>(ex.Current);
        Assert.Equal(2, current.RowVersion);
        Assert.Equal(Money.FromRupees(6), current.Postings[0].Amount);
    }

    [Fact]
    public async Task Cancel_keeps_the_number_and_it_is_not_reused()
    {
        var saved = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        await host.Vouchers.CancelAsync(owner, company, saved.Id, saved.RowVersion, "duplicate");
        var v = host.Vouchers.Get(owner, company, saved.Id);
        Assert.True(v.Cancelled);
        Assert.Empty(host.Observer.Saved.Last().Postings);
        Assert.Equal("RCT/26-27/00002", (await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5))).DisplayNumber);
        var ex = await Fails(ServiceError.Invalid, () => host.Vouchers.EditJournalAsync(owner, company, saved.Id, v.RowVersion, Receipt(May10, 5)));
        Assert.Equal("This voucher is cancelled.", ex.Message);
        await Fails(ServiceError.Invalid, () => host.Vouchers.CancelAsync(owner, company, saved.Id, v.RowVersion, "again"));
    }

    [Fact]
    public async Task Data_entry_sees_and_edits_only_their_own_vouchers()
    {
        var mine = host.NewUser("asha", BuiltInRole.DataEntry, company).Context;
        var other = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        var own = await host.Vouchers.SaveJournalAsync(mine, company, Receipt(May10, 6));
        Assert.Equal(ServiceError.PermissionDenied, Assert.Throws<ServiceException>(() => host.Vouchers.Get(mine, company, other.Id)).Error);
        Assert.Equal(own.Id, host.Vouchers.Get(mine, company, own.Id).Id);
        var range = new FinancialYear(2026).Range;
        Assert.Equal([own.Id], host.Vouchers.List(mine, company, range, null).Select(v => v.Id));
        Assert.Equal(2, host.Vouchers.List(owner, company, range, null).Count);
        await Fails(ServiceError.PermissionDenied, () => host.Vouchers.EditJournalAsync(mine, company, other.Id, other.RowVersion, Receipt(May10, 1)));
        await Fails(ServiceError.PermissionDenied, () => host.Vouchers.CancelAsync(mine, company, own.Id, own.RowVersion, "x"));
    }

    [Fact]
    public async Task Viewer_cannot_save()
    {
        var viewer = host.NewUser("ravi", BuiltInRole.Viewer, company).Context;
        await Fails(ServiceError.PermissionDenied, () => host.Vouchers.SaveJournalAsync(viewer, company, Receipt(May10, 5)));
    }

    [Fact]
    public async Task Failure_halfway_leaves_nothing_and_does_not_use_the_number()
    {
        host.Hub.BeforeAudit = _ => throw new IOException("disk full");
        await Assert.ThrowsAsync<IOException>(() => host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5)));
        host.Hub.BeforeAudit = null;
        Assert.Empty(host.Vouchers.List(owner, company, new FinancialYear(2026).Range, null));
        using (var c = host.Hub.Open(company)) Assert.Empty(VoucherRepository.AllPostings(c));
        Assert.Empty(host.Observer.Saved);
        Assert.Equal("RCT/26-27/00001", (await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5))).DisplayNumber);
    }

    [Fact]
    public async Task Save_edit_and_cancel_are_audited_with_before_and_after()
    {
        var saved = await host.Vouchers.SaveJournalAsync(owner, company, Receipt(May10, 5));
        var edited = await host.Vouchers.EditJournalAsync(owner, company, saved.Id, saved.RowVersion, Receipt(May10, 6));
        await host.Vouchers.CancelAsync(owner, company, saved.Id, edited.RowVersion, "duplicate");
        using var c = host.Hub.Open(company);
        var rows = AuditLog.Read(c, new AuditFilter(Entity: "voucher"));
        Assert.Equal(["cancel", "edit", "create"], rows.Select(r => r.Entry.Action));
        Assert.Null(rows[2].Entry.Before);
        Assert.NotNull(rows[1].Entry.Before);
        Assert.Contains("duplicate", rows[0].Entry.After);
        Assert.True(AuditLog.Verify(c).Ok);
    }
}
