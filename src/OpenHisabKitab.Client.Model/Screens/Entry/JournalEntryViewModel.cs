using System.Collections.ObjectModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>
/// RCPT, PYMT, JRNL, and CNTR (spec 6.3, Vouchers). RCPT and PYMT have an account (cash or bank) and lines of ledger and
/// amount: the account takes the balancing debit or credit. JRNL and CNTR use Debit and Credit columns; CNTR offers only
/// cash and bank. A receipt or payment line for a party is allocated to its bills, oldest first, and can be changed.
/// </summary>
public sealed class JournalEntryViewModel : VoucherEntryViewModel
{
    JournalEntryViewModel(ShellContext ctx, string code) : base(ctx, code)
    {
        if (code is "RCPT" or "PYMT")
        {
            Account = new LookupField<LedgerOption>(() => Ctx.Lookups.Ledgers(LedgerUse.CashOrBank), l => l.Name);
            Account.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Account.Value)) Edited();
            };
        }
        Clear();
        Ready();
    }

    public static async Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx)
    {
        var screen = new JournalEntryViewModel(ctx, cmd.Code);
        if (cmd.Args.Count > 0)
        {
            try { await screen.LoadNumberAsync(string.Join(' ', cmd.Args)); }
            catch (HostException e) { screen.Message = e.Message; }
        }
        return screen;
    }

    /// <summary>RCPT and PYMT: an account field and amount lines. JRNL and CNTR: Debit and Credit columns.</summary>
    public bool HasAccount => Code is "RCPT" or "PYMT";

    public bool HasDebitCredit => !HasAccount;

    public string AccountLabel => Code == "RCPT" ? Resources.Text.ReceivedInLabel : Resources.Text.PaidFromLabel;

    /// <summary>RCPT: received in. PYMT: paid from. Null for JRNL and CNTR.</summary>
    public LookupField<LedgerOption>? Account { get; }

    public ObservableCollection<EntryLine> Lines { get; } = [];

    LedgerUse Use => Code == "CNTR" ? LedgerUse.CashOrBank : LedgerUse.Any;

    public override object AddLine()
    {
        var line = NewLine();
        var at = CurrentLine is EntryLine current ? Lines.IndexOf(current) + 1 : Lines.Count;
        Lines.Insert(at, line);
        CurrentLine = line;
        return line;
    }

    protected override void RemoveLine(object line)
    {
        if (line is EntryLine l) Lines.Remove(l);
        if (Lines.Count == 0) Lines.Add(NewLine());
        Recalculate();
    }

    EntryLine NewLine()
    {
        var line = new EntryLine(() => Ctx.Lookups.Ledgers(Use));
        line.Changed += () =>
        {
            Recalculate();
            Edited();
        };
        return line;
    }

    protected override void Clear()
    {
        Account?.Set(null);
        Lines.Clear();
        Lines.Add(NewLine());
        if (!HasAccount) Lines.Add(NewLine());
        CurrentLine = null;
        Recalculate();
    }

    // Live totals (spec 6.4): the difference shows until it reaches zero.
    void Recalculate()
    {
        if (HasAccount)
        {
            Totals = Format(Text.TotalLine, IndianNumberFormat.Format(Sum(l => l.Amount)));
            Difference = "";
            return;
        }
        var (debit, credit) = (Sum(l => l.Debit), Sum(l => l.Credit));
        Totals = Format(Text.DebitCreditTotals, IndianNumberFormat.Format(debit), IndianNumberFormat.Format(credit));
        Difference = debit == credit ? "" : Format(Text.DifferenceLine, IndianNumberFormat.FormatDrCr(debit - credit));
    }

    Money Sum(Func<EntryLine, string> field) => Money.Sum(Lines.Select(l => Money.TryParse(field(l), out var m) ? m : Money.Zero));

    // ---- Bills -------------------------------------------------------------------------------------------------

    protected override bool CanAllocate => CurrentLine is EntryLine line && SettlesBills(line, out _);

    // A receipt credits its lines and a payment debits them; a party line settles bills when that lowers what is owed.
    bool SettlesBills(EntryLine line, out Money amount)
    {
        amount = Money.Zero;
        if (!HasAccount || line.Ledger.Value is not { IsParty: true } party || !Money.TryParse(line.Amount, out amount) || amount <= Money.Zero) return false;
        var posting = Code == "RCPT" ? -amount : amount;
        return !PartySides.IsBill(SideOf(party), posting);
    }

    static PartySide SideOf(LedgerOption party) => party.Nature == "Liability" ? PartySide.Payable : PartySide.Receivable;

    /// <summary>After a party line's amount is typed: the oldest first suggestion, from one call for the party's open bills.</summary>
    public async Task LineAmountCommittedAsync(EntryLine line)
    {
        if (!SettlesBills(line, out var amount))
        {
            if (line.Allocations.Count > 0) line.Allocations = [];
            return;
        }
        if (line.ChosenByHand || line.Allocations.Count > 0 && line.Settles == amount) return;
        await RunAsync(() => SuggestAsync(line, amount));
    }

    async Task SuggestAsync(EntryLine line, Money amount)
    {
        var panel = await PanelAsync(line, amount);
        panel.SuggestOldestFirst();
        Apply(line, panel, amount, byHand: false);
    }

    // Owner decision 1: the suggestion is applied automatically, so a line saved straight from its amount is still allocated.
    async Task SuggestUnallocatedAsync()
    {
        foreach (var line in Lines)
            if (SettlesBills(line, out var amount) && !line.ChosenByHand && (line.Allocations.Count == 0 || line.Settles != amount))
                await SuggestAsync(line, amount);
    }

    protected override async Task OpenBillsAsync()
    {
        if (CurrentLine is not EntryLine line || !SettlesBills(line, out var amount)) return;
        Bills = await PanelAsync(line, amount);
        openFor = (line, amount);
    }

    (EntryLine Line, Money Amount)? openFor;

    protected override void CloseBills()
    {
        if (Bills is { HasUnreadable: true }) return;
        if (Bills is { } panel && openFor is { } f) Apply(f.Line, panel, f.Amount, byHand: true);
        openFor = null;
        Bills = null;
    }

    async Task<BillsPanelViewModel> PanelAsync(EntryLine line, Money amount)
    {
        var party = line.Ledger.Value!;
        var bills = await Ctx.Host.CallAsync((h, o) => h.Vouchers.OpenBillsAsync(new OpenBillsRequest
        {
            Company = Ctx.Company!.Id, Party = party.Id, ExcludingVoucher = Existing?.Id ?? 0,
        }, o));
        line.BillNumbers = bills.Bills.ToDictionary(b => (b.Source, b.Id), b => b.Number);
        // Other lines of this voucher for the same party have already taken part of its bills.
        var taken = Lines.Where(l => l != line && l.Ledger.Value?.Id == party.Id).SelectMany(l => l.Allocations)
            .GroupBy(a => (a.BillSource, a.BillId)).ToDictionary(g => g.Key, g => g.Sum(a => a.Paise));
        var open = bills.Bills.Select(b => taken.TryGetValue((b.Source, b.Id), out var t) ? new BillMsg(b) { SettledPaise = b.SettledPaise + t } : b).ToList();
        return new BillsPanelViewModel(party.Name, party.Id, amount, open, line.Allocations, Ctx.Settings.Current.Text("ui.dateFormat"));
    }

    void Apply(EntryLine line, BillsPanelViewModel panel, Money amount, bool byHand)
    {
        line.Settles = amount;
        line.Allocations = panel.Result();
        line.ChosenByHand = byHand;
        Edited();
    }

    // ---- A new party from inside the voucher (spec 6.4) --------------------------------------------------------

    /// <summary>Opens PRTY as a sub screen with the typed name; on save the line takes the new party.</summary>
    public Task CreatePartyAsync(EntryLine line)
    {
        var side = Code == "PYMT" ? PartySide.Payable : PartySide.Receivable;
        var sub = PartiesViewModel.ForNew(Ctx, line.Ledger.Text.Trim(), side, async id =>
        {
            await Ctx.Lookups.RefreshAsync();
            line.Ledger.Set(Ctx.Lookups.Ledgers(LedgerUse.Any).FirstOrDefault(l => l.Id == id));
            SubScreen = null;
        }, () => SubScreen = null);
        SubScreen = sub;
        return sub.LoadAsync(CancellationToken.None);
    }

    // ---- Saving ------------------------------------------------------------------------------------------------

    protected override async Task<SavedReply?> SaveNewAsync(DateOnly date)
    {
        await SuggestUnallocatedAsync();
        if (Build(date) is not { } journal) return null;
        journal.RequestId = RequestId;
        return await Ctx.Host.CallAsync((h, o) => h.Vouchers.SaveJournalAsync(new JournalRequest { Company = Ctx.Company!.Id, Journal = journal }, o));
    }

    protected override async Task<SavedReply?> SaveEditAsync(DateOnly date, VoucherMsg existing)
    {
        await SuggestUnallocatedAsync();
        if (Build(date) is not { } journal) return null;
        return await Ctx.Host.CallAsync((h, o) => h.Vouchers.EditJournalAsync(new EditJournalRequest
        {
            Company = Ctx.Company!.Id, Id = existing.Id, RowVersion = existing.RowVersion, Journal = journal,
        }, o));
    }

    JournalMsg? Build(DateOnly date)
    {
        var msg = new JournalMsg { Type = VoucherType, Date = Wire(date), Narration = Narration.Trim() };
        var n = 0;
        var total = Money.Zero;
        foreach (var line in Lines.Where(l => !l.IsBlank))
        {
            n++;
            if (line.Ledger.Value is null && !line.Ledger.Commit() || line.Ledger.Value is not { } ledger)
            {
                Message = Format(Text.LineChooseLedger, n);
                return null;
            }
            Money amount;
            if (HasAccount)
            {
                if (!Money.TryParse(line.Amount, out var a))
                {
                    Message = Format(Text.LineAmountInvalid, n);
                    return null;
                }
                amount = Code == "RCPT" ? -a : a;
                total += a;
            }
            else
            {
                var debit = Money.Zero;
                var credit = Money.Zero;
                if (line.Debit.Trim().Length > 0 && !Money.TryParse(line.Debit, out debit) || line.Credit.Trim().Length > 0 && !Money.TryParse(line.Credit, out credit))
                {
                    Message = Format(Text.LineAmountInvalid, n);
                    return null;
                }
                amount = debit - credit;
            }
            msg.Lines.Add(new PostingLineMsg { Ledger = ledger.Id, Paise = amount.Paise });
        }
        // The host checks allocations by party, one per bill, so lines for the same party are added together.
        msg.Allocations.AddRange(Lines.Where(l => !l.IsBlank).SelectMany(l => l.Allocations)
            .GroupBy(a => (a.Party, a.BillSource, a.BillId))
            .Select(g => new AllocationMsg { Party = g.Key.Party, BillSource = g.Key.BillSource, BillId = g.Key.BillId, Paise = g.Sum(a => a.Paise) }));
        if (HasAccount)
        {
            if (Account!.Value is null && !Account.Commit() || Account.Value is not { } account)
            {
                Message = Code == "RCPT" ? Text.ChooseReceivedIn : Text.ChoosePaidFrom;
                return null;
            }
            msg.Lines.Insert(0, new PostingLineMsg { Ledger = account.Id, Paise = (Code == "RCPT" ? total : -total).Paise });
        }
        return msg;
    }

    /// <summary>Receipts, payments, journals, and contras print as a voucher slip (Task 7).</summary>
    protected override Task<PrintResult> BuildPrintAsync(VoucherMsg stored, PrintTarget target) =>
        Task.FromResult(PrintResult.Refused(Text.NothingToPrint));

    protected override async Task FillAsync(VoucherMsg v)
    {
        Lines.Clear();
        var postings = v.Postings.ToList();
        if (HasAccount)
        {
            // The account is the cash or bank line on the voucher's own side: debited by a receipt, credited by a payment.
            var account = postings.FirstOrDefault(p => (Code == "RCPT" ? p.Paise > 0 : p.Paise < 0)
                && Ctx.Lookups.Ledgers(LedgerUse.CashOrBank).Any(l => l.Id == p.Ledger));
            Account!.Set(account is null ? null : Ctx.Lookups.Ledgers(LedgerUse.Any).FirstOrDefault(l => l.Id == account.Ledger));
            if (account is not null) postings.Remove(account);
        }
        foreach (var p in postings)
        {
            var line = NewLine();
            line.Ledger.Set(Ctx.Lookups.Ledgers(LedgerUse.Any).FirstOrDefault(l => l.Id == p.Ledger));
            var amount = new Money(Math.Abs(p.Paise));
            // RCPT lines are credits and PYMT lines debits. A line on the other side (a second debit on a receipt, such as
            // bank charges) shows with a minus, which Build reads back as the same posting.
            if (HasAccount) line.Amount = IndianNumberFormat.Format(new Money(Code == "RCPT" ? -p.Paise : p.Paise), NegativeStyle.Minus);
            else if (p.Paise > 0) line.Debit = IndianNumberFormat.Format(amount);
            else line.Credit = IndianNumberFormat.Format(amount);
            var mine = v.Allocations.Where(a => a.Party == p.Ledger).ToList();
            if (mine.Count > 0)
            {
                line.Settles = amount;
                line.Allocations = mine;
                line.ChosenByHand = true;
            }
            Lines.Add(line);
        }
        if (Lines.Count == 0) Lines.Add(NewLine());
        Recalculate();
        // The bill numbers come from the party's open bills, one call for each party line that settles bills.
        foreach (var line in Lines.Where(l => l.Allocations.Count > 0 && l.Ledger.Value is not null))
        {
            var bills = await Ctx.Host.CallAsync((h, o) => h.Vouchers.OpenBillsAsync(new OpenBillsRequest
            {
                Company = Ctx.Company!.Id, Party = line.Ledger.Value!.Id, ExcludingVoucher = v.Id,
            }, o));
            line.BillNumbers = bills.Bills.ToDictionary(b => (b.Source, b.Id), b => b.Number);
            line.Allocations = [.. line.Allocations];
        }
    }
}
