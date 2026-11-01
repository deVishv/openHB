using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class JournalEntryTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<JournalEntryViewModel> Open(ShellContext ctx, string input)
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var s = (JournalEntryViewModel)(await JournalEntryViewModel.CreateAsync(cmd, ctx))!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    static LedgerOption Ledger(ShellContext ctx, string name) => ctx.Lookups.Ledgers(LedgerUse.Any).Single(l => l.Name == name);

    static LedgerOption Required(ShellContext ctx, string required) => ctx.Lookups.Ledgers(LedgerUse.Any).Single(l => l.Required == required);

    async Task<int> NewParty(ShellViewModel shell, string name)
    {
        var company = shell.Context.Company!.Id;
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var id = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = company, Name = name, Group = group, Party = new PartyMsg { State = "27", CreditDays = 30 },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(id));
        return id;
    }

    async Task<SavedReply> Sale(ShellViewModel shell, int party, long paise, string date)
    {
        var sales = Required(shell.Context, "Sales").Id;
        return await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = shell.Context.Company!.Id,
            Invoice = new InvoiceMsg
            {
                Type = "Sale", Date = date, Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
                Lines = { new InvoiceLineMsg { Ledger = sales, Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = paise } },
            },
        }, t.Headers());
    }

    static void Line(EntryLine line, LedgerOption ledger, string amount = "", string debit = "", string credit = "")
    {
        line.Ledger.Set(ledger);
        line.Amount = amount;
        line.Debit = debit;
        line.Credit = credit;
    }

    [Fact]
    public async Task Receipt_saves_and_clears_for_the_next()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "1,500");
        s.Narration = "counter sale";
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.Equal("Saved RCT/26-27/00001.", shell.Status.Message);
        Assert.Equal("", s.Lines[0].Amount);
        Assert.Equal("", s.Narration);
        Assert.False(s.HasUnsavedEdits);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal([150000L, -150000L], v.Postings.OrderByDescending(p => p.Paise).Select(p => p.Paise));
    }

    [Fact]
    public async Task Journal_shows_the_difference_until_balanced()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "JRNL");
        Line(s.Lines[0], Required(shell.Context, "Cash"), debit: "1,000");
        Line(s.Lines[1], Required(shell.Context, "Capital"), credit: "400");
        Assert.Equal("Debit 1,000.00  Credit 400.00", s.Totals);
        Assert.Equal("Difference 600.00 Dr", s.Difference);
        s.Lines[1].Credit = "1000";
        Assert.Equal("", s.Difference);
    }

    [Fact]
    public async Task Contra_offers_only_cash_and_bank()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "CNTR");
        s.Lines[0].Ledger.Text = "sales";
        Assert.False(s.Lines[0].Ledger.Commit());
        s.Lines[0].Ledger.Text = "cash";
        Assert.True(s.Lines[0].Ledger.Commit());
        Assert.Null(s.Account);
    }

    [Fact]
    public async Task Receipt_party_line_suggests_oldest_first()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        var later = await Sale(shell, party, 10000_00, "2026-06-01");
        var earlier = await Sale(shell, party, 8000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "15,000");
        await s.LineAmountCommittedAsync(s.Lines[0]);
        Assert.Equal([(earlier.Id, 8000_00L), (later.Id, 7000_00L)], s.Lines[0].Allocations.Select(a => (a.BillId, a.Paise)));
        Assert.Equal("INV/26-27/00002 8,000.00; INV/26-27/00001 7,000.00; on account 0.00", s.Lines[0].AllocationText);
        await s.SaveAsync();
        Assert.Null(s.Message);
        var bills = await t.OtherOwnerClient().Vouchers.OpenBillsAsync(new OpenBillsRequest { Company = shell.Context.Company!.Id, Party = party }, t.Headers());
        Assert.Equal(3000_00, bills.Bills.Single(b => b.Id == later.Id).AmountPaise - bills.Bills.Single(b => b.Id == later.Id).SettledPaise);
    }

    [Fact]
    public async Task Changing_an_allocation_in_the_panel_is_saved()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        var sale = await Sale(shell, party, 10000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "5,000");
        s.CurrentLine = s.Lines[0];
        Assert.True(s.CanRun("voucher.bills"));
        await s.RunAsync("voucher.bills");
        Assert.NotNull(s.Bills);
        s.Bills!.Rows.Single().Allocate = "2,000";
        Assert.Equal("On account 3,000.00", s.Bills.OnAccount);
        Assert.True(s.Cancel());
        Assert.Null(s.Bills);
        await s.SaveAsync();
        Assert.Null(s.Message);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal((sale.Id, 2000_00L), (v.Allocations.Single().BillId, v.Allocations.Single().Paise));
    }

    [Fact]
    public async Task Panel_shows_more_than_open_in_host_words()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        await Sale(shell, party, 1000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "5,000");
        s.CurrentLine = s.Lines[0];
        await s.RunAsync("voucher.bills");
        s.Bills!.Rows.Single().Allocate = "1,000.01";
        Assert.Equal("Bill INV/26-27/00001 has only 1,000.00 open.", s.Bills.Problem);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_request_id_and_a_retry_saves_once()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "100");
        var id = s.RequestId;
        t.FailReplyOf = ("SaveJournal", 1);
        await s.SaveAsync();
        Assert.NotNull(s.Message);
        Assert.Equal(id, s.RequestId);
        Assert.Equal("100", s.Lines[0].Amount);
        t.FailReplyOf = null;
        for (var i = 0; i < 6 && shell.Context.Host.State != Model.Connection.ConnectionState.Connected; i++)
        {
            await t.Settle(shell.Context.Host);
            t.Time.Advance(TimeSpan.FromSeconds(2));
            await t.Settle(shell.Context.Host);
        }
        s.Lines[0].Amount = "250";
        await s.SaveAsync();
        Assert.Equal("Already saved as RCT/26-27/00001. Showing the saved version.", s.Message);
        Assert.True(s.IsEditing);
        Assert.Equal("100.00", s.Lines[0].Amount);
        var list = await t.OtherOwnerClient().Vouchers.ListAsync(new ListVouchersRequest
        {
            Company = shell.Context.Company!.Id, Period = new Period { From = "2026-04-01", To = "2027-03-31" },
        }, t.Headers());
        Assert.Single(list.Vouchers);
    }

    [Fact]
    public async Task Editing_by_number_loads_and_saves_the_edit()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "100");
        await s.SaveAsync();
        var edit = await Open(shell.Context, "RCPT RCT/26-27/00001");
        Assert.True(edit.IsEditing);
        Assert.Equal("RCT/26-27/00001", edit.Subject);
        Assert.Equal("Cash", edit.Account!.Value!.Name);
        Assert.Equal("100.00", edit.Lines[0].Amount);
        edit.Lines[0].Amount = "120";
        await edit.SaveAsync();
        Assert.Null(edit.Message);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal((2L, 12000L), (v.RowVersion, v.Postings.Max(p => p.Paise)));
    }

    [Fact]
    public async Task Edit_conflict_shows_the_current_version()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "100");
        await s.SaveAsync();
        var edit = await Open(shell.Context, "RCPT RCT/26-27/00001");
        var other = await Open(shell.Context, "RCPT RCT/26-27/00001");
        other.Lines[0].Amount = "300";
        await other.SaveAsync();
        edit.Lines[0].Amount = "200";
        await edit.SaveAsync();
        Assert.Equal("Someone else changed this voucher. Showing the current version.", edit.Message);
        Assert.Equal("300.00", edit.Lines[0].Amount);
    }

    [Fact]
    public async Task Cancel_voucher_asks_then_cancels()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "100");
        await s.SaveAsync();
        var edit = await Open(shell.Context, "RCPT RCT/26-27/00001");
        Assert.True(edit.CanRun("voucher.cancelVoucher"));
        await edit.RunAsync("voucher.cancelVoucher");
        Assert.Equal("Cancel voucher RCT/26-27/00001?", confirm.LastQuestion);
        Assert.True(edit.IsCancelled);
        Assert.False(edit.CanRun("voucher.cancelVoucher"));
    }

    [Fact]
    public async Task Escape_asks_once_before_discarding()
    {
        var confirm = new FakeConfirm(true);
        var shell = await t.LoggedInShellAsync([], company: "A Traders", confirm: confirm);
        var s = await Open(shell.Context, "JRNL");
        Assert.False(s.Cancel());
        Line(s.Lines[0], Required(shell.Context, "Cash"), debit: "10");
        Assert.True(s.HasUnsavedEdits);
        Assert.True(s.Cancel());
        await t.Eventually(() => !s.HasUnsavedEdits);
        Assert.Equal(1, confirm.Asked);
        Assert.Equal("Discard this voucher?", confirm.LastQuestion);
    }

    [Fact]
    public async Task Unknown_party_opens_PRTY_and_returns_with_it()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "RCPT");
        s.Lines[0].Ledger.Text = "Blr Mart";
        Assert.False(s.Lines[0].Ledger.Commit());
        Assert.True(s.Lines[0].Ledger.OffersCreate);
        await s.CreatePartyAsync(s.Lines[0]);
        var sub = Assert.IsType<PartiesViewModel>(s.SubScreen);
        Assert.Equal("Blr Mart", sub.FormName);
        await sub.SaveAsync();
        Assert.Null(sub.Message);
        Assert.Null(s.SubScreen);
        Assert.Equal("Blr Mart", s.Lines[0].Ledger.Value!.Name);
    }

    [Fact]
    public async Task Locked_date_shows_the_host_message_and_keeps_the_entry()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        await t.OtherOwnerClient().Admin.LockUpToAsync(new LockRequest { Company = shell.Context.Company!.Id, Date = "2026-06-30" }, t.Headers());
        var s = await Open(shell.Context, "RCPT");
        s.Date = "15-06-2026";
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Required(shell.Context, "Sales"), amount: "100");
        await s.SaveAsync();
        Assert.Equal("Books are locked up to 30-06-2026.", s.Message);
        Assert.Equal("100", s.Lines[0].Amount);
    }

    [Fact]
    public async Task Opens_with_at_most_three_calls()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var before = t.Requests.Count;
        await Open(shell.Context, "JRNL");
        Assert.True(t.Requests.Count - before <= 3);
    }
    [Fact]
    public async Task Resaving_a_receipt_with_two_debits_keeps_every_line()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        var company = shell.Context.Company!.Id;
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = company }, t.Headers());
        var bank = (int)(await other.Masters.AddLedgerAsync(new AddLedgerRequest { Company = company, Name = "HDFC Bank", Group = chart.Groups.Single(g => g.Kind == "BankAccounts").Id }, t.Headers())).Id;
        var charges = (int)(await other.Masters.AddLedgerAsync(new AddLedgerRequest { Company = company, Name = "Bank charges", Group = chart.Groups.Single(g => g.Kind == "OtherExpenses").Id }, t.Headers())).Id;
        await other.Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = company,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10",
                Lines = { new PostingLineMsg { Ledger = bank, Paise = 9900_00 }, new PostingLineMsg { Ledger = charges, Paise = 100_00 }, new PostingLineMsg { Ledger = party, Paise = -10000_00 } },
            },
        }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.Chart.Ledgers.Any(l => l.Id == charges));
        var edit = await Open(shell.Context, "RCPT RCT/26-27/00001");
        edit.Narration = "fixed";
        await edit.SaveAsync();
        Assert.Null(edit.Message);
        var v = await other.Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = company, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal([(bank, 9900_00L), (charges, 100_00L), (party, -10000_00L)],
            v.Postings.Select(p => (p.Ledger, p.Paise)).OrderByDescending(x => x.Item2));
    }

    [Fact]
    public async Task Saving_straight_from_the_amount_still_allocates_oldest_first()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        var sale = await Sale(shell, party, 10000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "4,000");
        await s.SaveAsync();
        Assert.Null(s.Message);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal((sale.Id, 4000_00L), (v.Allocations.Single().BillId, v.Allocations.Single().Paise));
    }

    [Fact]
    public async Task Changing_a_lines_party_drops_its_allocations()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var ravi = await NewParty(shell, "Ravi Stores");
        await NewParty(shell, "Blr Mart");
        await Sale(shell, ravi, 10000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "1,000");
        await s.LineAmountCommittedAsync(s.Lines[0]);
        Assert.NotEmpty(s.Lines[0].Allocations);
        s.Lines[0].Ledger.Set(Ledger(shell.Context, "Blr Mart"));
        Assert.Empty(s.Lines[0].Allocations);
        await s.SaveAsync();
        Assert.Null(s.Message);
    }
    [Fact]
    public async Task Two_lines_for_one_party_share_its_bills_and_save()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        var first = await Sale(shell, party, 1000_00, "2026-05-01");
        var second = await Sale(shell, party, 1000_00, "2026-05-02");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "600");
        await s.LineAmountCommittedAsync(s.Lines[0]);
        var line2 = (EntryLine)s.AddLine();
        Line(line2, Ledger(shell.Context, "Ravi Stores"), amount: "600");
        await s.LineAmountCommittedAsync(line2);
        Assert.Equal([(first.Id, 400_00L), (second.Id, 200_00L)], line2.Allocations.Select(a => (a.BillId, a.Paise)));
        await s.SaveAsync();
        Assert.Null(s.Message);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "Receipt", Number = "RCT/26-27/00001" }, t.Headers());
        Assert.Equal([(first.Id, 1000_00L), (second.Id, 200_00L)], v.Allocations.OrderBy(a => a.BillId).Select(a => (a.BillId, a.Paise)));
    }

    [Fact]
    public async Task The_bills_panel_stays_open_on_text_it_cannot_read()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await NewParty(shell, "Ravi Stores");
        await Sale(shell, party, 1000_00, "2026-05-01");
        var s = await Open(shell.Context, "RCPT");
        s.Account!.Set(Required(shell.Context, "Cash"));
        Line(s.Lines[0], Ledger(shell.Context, "Ravi Stores"), amount: "1,000");
        s.CurrentLine = s.Lines[0];
        await s.RunAsync("voucher.bills");
        s.Bills!.Rows.Single().Allocate = "1,0O0";
        Assert.Equal("Bill INV/26-27/00001: enter an amount like 12,345.60.", s.Bills.Problem);
        Assert.True(s.Cancel());
        Assert.NotNull(s.Bills);
        s.Bills.Rows.Single().Allocate = "1,000";
        Assert.True(s.Cancel());
        Assert.Null(s.Bills);
    }

}
