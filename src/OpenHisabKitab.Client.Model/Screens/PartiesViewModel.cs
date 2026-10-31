using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>An opening bill as typed (spec 13, Opening balances). Id is 0 for a new bill.</summary>
public sealed partial class OpeningBillLine : ObservableObject
{
    public long Id { get; init; }

    [ObservableProperty]
    public partial string Number { get; set; } = "";

    [ObservableProperty]
    public partial string Date { get; set; } = "";

    [ObservableProperty]
    public partial string DueDate { get; set; } = "";

    [ObservableProperty]
    public partial string Amount { get; set; } = "";

    public bool IsBlank => Number.Trim().Length == 0 && Date.Trim().Length == 0 && DueDate.Trim().Length == 0 && Amount.Trim().Length == 0;
}

/// <summary>A party in the list, with its group and GSTIN.</summary>
public sealed record PartyRow(int Ledger, string Name, string Group, string Gstin, string State);

/// <summary>
/// PRTY: customers and suppliers with GSTIN, state, address, credit days, and opening bills (spec 6.3, Masters; spec 13,
/// Opening balances). Opened from a voucher with ForNew it is a sub screen that hands back the new party (spec 6.4).
/// </summary>
public sealed partial class PartiesViewModel : ScreenViewModel, IScreenCommands, IEntryLines
{
    static readonly GstData Gst = GstData.LoadEmbedded();
    string? query;
    PartyMsg? editing;
    long billsVersion = 1;
    string billsAsLoaded = "";
    readonly Func<int, Task>? saved;
    readonly Action? discarded;

    PartiesViewModel(ShellContext ctx, string? query, Func<int, Task>? saved = null, Action? discarded = null) : base(ctx)
    {
        this.query = query;
        this.saved = saved;
        this.discarded = discarded;
        Ctx.Lookups.Changed += Refresh;
    }

    public static PartiesViewModel Create(ParsedCommand cmd, ShellContext ctx) =>
        new(ctx, cmd.Args.Count == 0 ? null : string.Join(' ', cmd.Args));

    /// <summary>The sub screen a voucher opens to create a party (spec 6.4). saved gets the new party's ledger id; discarded runs on Esc.</summary>
    public static PartiesViewModel ForNew(ShellContext ctx, string name, PartySide side, Func<int, Task> saved, Action? discarded = null)
    {
        var s = new PartiesViewModel(ctx, null, saved, discarded) { IsSubScreen = true };
        s.Refresh();
        s.FormName = name;
        s.FormGroup = s.Groups.FirstOrDefault(g => g.Kind == (side == PartySide.Receivable ? "TradeReceivables" : "TradePayables"));
        s.FormState = s.States.FirstOrDefault(x => x.Code == ctx.Lookups.Profile?.State);
        return s;
    }

    public override string Code => "PRTY";

    public bool IsSubScreen { get; private init; }

    public override bool HasUnsavedEdits => editing is null && FormName.Trim().Length > 0 || BillsText() != billsAsLoaded;

    public IReadOnlyList<StateOption> States => StateOption.All;

    public bool CanEditOpenings => Ctx.Has("EditOpeningsAndRequiredLedgers");

    /// <summary>Groups a party may belong to: Trade Receivables, Trade Payables, and groups under them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GroupMsg> Groups { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<PartyRow> Parties { get; private set; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial PartyRow? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial string FormName { get; set; } = "";

    [ObservableProperty]
    public partial GroupMsg? FormGroup { get; set; }

    [ObservableProperty]
    public partial string FormGstin { get; set; } = "";

    [ObservableProperty]
    public partial StateOption? FormState { get; set; }

    [ObservableProperty]
    public partial string FormCreditDays { get; set; } = "";

    [ObservableProperty]
    public partial string FormAddress { get; set; } = "";

    public ObservableCollection<OpeningBillLine> OpeningBills { get; } = [];

    /// <summary>"Opening balance 5,000.00 Dr; bills 3,000.00; on account 2,000.00 Dr" for a saved party, else "".</summary>
    [ObservableProperty]
    public partial string OpeningSummary { get; private set; } = "";

    public object? CurrentLine { get; set; }

    public event Action? FindRequested;

    public override void Find() => FindRequested?.Invoke();

    public object AddLine()
    {
        var line = new OpeningBillLine();
        var at = CurrentLine is OpeningBillLine current ? OpeningBills.IndexOf(current) + 1 : OpeningBills.Count;
        OpeningBills.Insert(at, line);
        CurrentLine = line;
        return line;
    }

    public override Task LoadAsync(CancellationToken ct)
    {
        Refresh();
        if (query is { } q)
        {
            query = null;
            var match = NameMatcher.Match(q, [.. Parties.Select(p => p.Name)]);
            if (match.Kind is not (MatchKind.None or MatchKind.Ambiguous)) Selected = Parties.First(p => p.Name == match.Candidates[0]);
        }
        return Task.CompletedTask;
    }

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind == "Masters";

    public bool CanRun(string command) => command switch
    {
        "record.new" => !IsSubScreen,
        "line.add" => CanEditOpenings,
        "line.remove" => CanEditOpenings && CurrentLine is OpeningBillLine,
        _ => false,
    };

    public Task RunAsync(string command)
    {
        switch (command)
        {
            case "record.new":
                Selected = null;
                Load(null, null);
                break;
            case "line.add":
                AddLine();
                break;
            case "line.remove" when CurrentLine is OpeningBillLine line:
                OpeningBills.Remove(line);
                CurrentLine = OpeningBills.LastOrDefault();
                OnPropertyChanged(nameof(HasUnsavedEdits));
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>Esc in the sub screen: asks once when anything was typed, then returns to the voucher.</summary>
    public override bool Cancel()
    {
        if (!IsSubScreen) return false;
        _ = DiscardAsync();
        return true;
    }

    async Task DiscardAsync()
    {
        if (FormName.Trim().Length > 0 && !await Ctx.Confirm.AskAsync(Text.DiscardParty)) return;
        discarded?.Invoke();
    }

    /// <summary>Ctrl+S: the party, then its opening bills when they changed.</summary>
    public override async Task SaveAsync()
    {
        Message = null;
        if (!EntryNumbers.TryDays(FormCreditDays, out var days))
        {
            Message = Text.CreditDaysInvalid;
            return;
        }
        (IReadOnlyList<OpeningBillMsg>? Rows, string? Error)? bills = BillsChanged() ? ParsedBills() : null;
        if (bills is { Error: { } error })
        {
            Message = error;
            return;
        }
        var company = Ctx.Company!.Id;
        var party = new PartyMsg
        {
            Ledger = editing?.Ledger ?? 0, RowVersion = editing?.RowVersion ?? 0, Gstin = FormGstin.Trim().ToUpperInvariant(),
            State = FormState?.Code ?? "", CreditDays = days, Address = FormAddress.Trim(),
        };
        var request = new SavePartyRequest { Company = company, Name = FormName.Trim(), Group = FormGroup?.Id ?? 0, Party = party };
        Busy = true;
        try
        {
            var id = (int)(await Ctx.Host.CallAsync((h, o) => h.Masters.SavePartyAsync(request, o))).Id;
            editing = party.Clone();
            editing.Ledger = id;
            editing.RowVersion = party.RowVersion + 1;
            if (bills is { Rows: { } rows })
            {
                var set = new SetOpeningBillsRequest { Company = company, Party = id, Version = billsVersion, Bills = { rows } };
                billsVersion = (await Ctx.Host.CallAsync((h, o) => h.Masters.SetOpeningBillsAsync(set, o))).Version;
                billsAsLoaded = BillsText();
            }
            OnPropertyChanged(nameof(HasUnsavedEdits));
            if (saved is not null) await saved(id);
        }
        catch (HostException e) { Message = e.Message; }
        finally { Busy = false; }
    }

    partial void OnSelectedChanged(PartyRow? value)
    {
        if (value is null) return;
        var party = Ctx.Lookups.Parties.GetValueOrDefault(value.Ledger);
        Load(party, value.Name);
        _ = RunAsync(async () =>
        {
            var list = await Ctx.Host.CallAsync((h, o) => h.Masters.GetOpeningBillsAsync(new LedgerRef { Company = Ctx.Company!.Id, Ledger = value.Ledger }, o));
            if (Selected?.Ledger != value.Ledger) return;
            billsVersion = list.Version;
            OpeningBills.Clear();
            foreach (var b in list.Bills)
                OpeningBills.Add(new OpeningBillLine
                {
                    Id = b.Id, Number = b.Number, Date = ShowDate(b.Date), DueDate = b.DueDate.Length == 0 ? "" : ShowDate(b.DueDate),
                    Amount = IndianNumberFormat.Format(new Money(b.AmountPaise)),
                });
            billsAsLoaded = BillsText();
            Summarise();
        });
    }

    partial void OnFormGstinChanged(string value)
    {
        // A valid GSTIN names its state in its first two digits; the host still checks they match.
        if (Gstin.TryParse(value.Trim().ToUpperInvariant(), Gst, out var g)) FormState = States.FirstOrDefault(s => s.Code == g.State.Value) ?? FormState;
    }

    partial void OnFilterChanged(string value) => Refresh();

    void Load(PartyMsg? party, string? name)
    {
        editing = party;
        FormName = name ?? "";
        var ledger = party is null ? null : Ctx.Lookups.Chart.Ledgers.FirstOrDefault(l => l.Id == party.Ledger);
        FormGroup = Groups.FirstOrDefault(g => g.Id == ledger?.Group) ?? Groups.FirstOrDefault(g => g.Kind == "TradeReceivables");
        FormGstin = party?.Gstin ?? "";
        FormState = States.FirstOrDefault(s => s.Code == (party?.State ?? Ctx.Lookups.Profile?.State));
        FormCreditDays = party is null || party.CreditDays == 0 ? "" : party.CreditDays.ToString(CultureInfo.InvariantCulture);
        FormAddress = party?.Address ?? "";
        OpeningBills.Clear();
        // A new party's bills start at version 1, as the host stores them; a saved party's version comes with its bills.
        billsVersion = party is null ? 1 : 0;
        billsAsLoaded = "";
        OpeningSummary = "";
        OnPropertyChanged(nameof(HasUnsavedEdits));
    }

    void Refresh()
    {
        var chart = Ctx.Lookups.Chart;
        var receivable = chart.Groups.FirstOrDefault(g => g.Kind == "TradeReceivables")?.Id;
        var payable = chart.Groups.FirstOrDefault(g => g.Kind == "TradePayables")?.Id;
        var byId = chart.Groups.ToDictionary(g => g.Id);
        bool Under(int id)
        {
            var seen = new HashSet<int>();
            for (var g = id; seen.Add(g) && byId.TryGetValue(g, out var group); g = group.Parent)
                if (g == receivable || g == payable) return true;
            return false;
        }
        Groups = [.. chart.Groups.Where(g => Under(g.Id))];
        var groupNames = chart.Groups.ToDictionary(g => g.Id, g => g.Name);
        var filter = Filter.Trim();
        Parties = [.. Ctx.Lookups.Ledgers(LedgerUse.Party)
            .Where(l => filter.Length == 0 || l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(l => (Ledger: l, Party: Ctx.Lookups.Parties[l.Id]))
            .Select(x => new PartyRow(x.Ledger.Id, x.Ledger.Name, x.Ledger.Group, x.Party.Gstin, x.Party.State))];
        if (editing is { Ledger: > 0 } e && Ctx.Lookups.Parties.TryGetValue(e.Ledger, out var fresh) && fresh.RowVersion >= e.RowVersion) editing = fresh;
        Summarise();
    }

    // The ledger opening is set in ACCT; the bills are its detail; any difference is on account (owner review, Plan 5 item 2).
    void Summarise()
    {
        if (editing is not { Ledger: > 0 } e || Ctx.Lookups.Chart.Ledgers.FirstOrDefault(l => l.Id == e.Ledger) is not { } ledger)
        {
            OpeningSummary = "";
            return;
        }
        var receivable = Ctx.Lookups.Chart.Groups.FirstOrDefault(g => g.Id == FormGroup?.Id)?.Nature != "Liability";
        var bills = Money.Sum(OpeningBills.Select(b => Money.TryParse(b.Amount, out var m) ? m : Money.Zero));
        var opening = new Money(ledger.OpeningPaise);
        var onAccount = opening - (receivable ? bills : -bills);
        OpeningSummary = string.Format(CultureInfo.InvariantCulture, Text.OpeningBillsSummary,
            IndianNumberFormat.FormatDrCr(opening), IndianNumberFormat.Format(bills), IndianNumberFormat.FormatDrCr(onAccount));
    }

    bool BillsChanged() => CanEditOpenings && BillsText() != billsAsLoaded;

    string BillsText() => string.Join("\n", OpeningBills.Where(b => !b.IsBlank).Select(b => $"{b.Id}|{b.Number}|{b.Date}|{b.DueDate}|{b.Amount}"));

    (IReadOnlyList<OpeningBillMsg>? Rows, string? Error) ParsedBills()
    {
        var rows = new List<OpeningBillMsg>();
        var today = DateOnly.FromDateTime(Ctx.Time.GetLocalNow().DateTime);
        var n = 0;
        foreach (var b in OpeningBills.Where(b => !b.IsBlank))
        {
            n++;
            string Line(string text) => string.Format(CultureInfo.InvariantCulture, text, n);
            if (!DateInput.TryParse(b.Date, today, out var date)) return (null, Line(Text.LineDateInvalid));
            DateOnly? due = null;
            if (b.DueDate.Trim().Length > 0)
            {
                if (!DateInput.TryParse(b.DueDate, today, out var d)) return (null, Line(Text.LineDateInvalid));
                due = d;
            }
            if (!Money.TryParse(b.Amount, out var amount)) return (null, Line(Text.LineAmountInvalid));
            rows.Add(new OpeningBillMsg
            {
                Id = b.Id, Number = b.Number.Trim(), Date = Wire(date), DueDate = due is { } x ? Wire(x) : "", AmountPaise = amount.Paise,
            });
        }
        return (rows, null);
    }

    static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    string ShowDate(string wire) =>
        DateText.Format(DateOnly.ParseExact(wire, "yyyy-MM-dd", CultureInfo.InvariantCulture), Ctx.Settings.Current.Text("ui.dateFormat"));
}
