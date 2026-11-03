using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>
/// SALE, PURC, CRNT, and DBNT (spec 6.4): party, place of supply defaulted from the party, item lines, reverse charge for
/// documents received, the original invoice for notes, and export details. Totals and tax update live with Core's
/// calculation. A note that lowers what the party owes is allocated to its bills, its original invoice first.
/// </summary>
public sealed partial class InvoiceEntryViewModel : VoucherEntryViewModel
{
    public const string Issued = "Issued";
    public const string Received = "Received";

    InvoiceEntryViewModel(ShellContext ctx, string code) : base(ctx, code)
    {
        Party = new LookupField<LedgerOption>(() => Ctx.Lookups.Ledgers(LedgerUse.Party), l => l.Name);
        Party.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Party.Value)) return;
            if (Party.Value is { } p && Ctx.Lookups.Parties.TryGetValue(p.Id, out var party))
                PlaceOfSupply = StateOption.All.FirstOrDefault(s => s.Code == party.State) ?? PlaceOfSupply;
            Allocations = [];
            allocationsByHand = false;
            Changed();
        };
        Direction = code == "PURC" ? Received : Issued;
        Clear();
        Ready();
    }

    public static async Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx)
    {
        var screen = new InvoiceEntryViewModel(ctx, cmd.Code);
        if (cmd.Args.Count > 0)
        {
            try { await screen.LoadNumberAsync(string.Join(' ', cmd.Args)); }
            catch (HostException e) { screen.Message = e.Message; }
        }
        return screen;
    }

    public LookupField<LedgerOption> Party { get; }

    public IReadOnlyList<StateOption> States => StateOption.All;

    [ObservableProperty]
    public partial StateOption? PlaceOfSupply { get; set; }

    /// <summary>The sales invoice types of spec 13, by their plain names.</summary>
    public IReadOnlyList<ChoiceOption> KindOptions { get; } =
    [
        new(nameof(InvoiceKind.Regular), Text.KindRegular), new(nameof(InvoiceKind.ExportWithTax), Text.KindExportWithTax),
        new(nameof(InvoiceKind.ExportUnderLut), Text.KindExportUnderLut), new(nameof(InvoiceKind.SezWithTax), Text.KindSezWithTax),
        new(nameof(InvoiceKind.SezWithoutTax), Text.KindSezWithoutTax), new(nameof(InvoiceKind.DeemedExport), Text.KindDeemedExport),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsExport), nameof(ShowsCountry))]
    public partial string Kind { get; set; } = nameof(InvoiceKind.Regular);

    public IReadOnlyList<ChoiceOption> DirectionOptions { get; } = [new(Issued, Text.DirectionIssued), new(Received, Text.DirectionReceived)];

    /// <summary>"Issued" for documents this business issues (outward), "Received" for those it receives. Fixed for SALE and PURC.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutward), nameof(ShowsKind), nameof(ShowsReverseCharge), nameof(ShowsPartyDoc), nameof(ShowsExport),
        nameof(ShowsShipTo), nameof(ShowsCountry))]
    public partial string Direction { get; set; }

    [ObservableProperty]
    public partial bool ReverseCharge { get; set; }

    [ObservableProperty]
    public partial string PartyDocNumber { get; set; } = "";

    [ObservableProperty]
    public partial string PartyDocDate { get; set; } = "";

    [ObservableProperty]
    public partial string OriginalNumber { get; set; } = "";

    [ObservableProperty]
    public partial string OriginalDate { get; set; } = "";

    [ObservableProperty]
    public partial string PortCode { get; set; } = "";

    [ObservableProperty]
    public partial string ShippingBillNumber { get; set; } = "";

    [ObservableProperty]
    public partial string ShippingBillDate { get; set; } = "";

    /// <summary>Where goods go when that differs from the buyer's address (Rule 46(o)); issued documents only.</summary>
    [ObservableProperty]
    public partial string ShipToName { get; set; } = "";

    [ObservableProperty]
    public partial string ShipToAddress { get; set; } = "";

    [ObservableProperty]
    public partial StateOption? ShipToState { get; set; }

    /// <summary>The country of destination, which export invoices must name (the Rule 46 export proviso).</summary>
    [ObservableProperty]
    public partial string ShipToCountry { get; set; } = "";

    public ObservableCollection<InvoiceLine> Lines { get; } = [];

    [ObservableProperty]
    public partial InvoiceTotalsView TotalsView { get; private set; } = InvoiceTotalsView.Empty;

    /// <summary>The bills a note settles; what is not allocated stays on account.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllocationText))]
    public partial IReadOnlyList<AllocationMsg> Allocations { get; private set; } = [];

    IReadOnlyDictionary<(string, long), string> billNumbers = new Dictionary<(string, long), string>();

    // True once the user changed the allocation in the bills panel, or for a stored note's own; the suggestion leaves it alone.
    bool allocationsByHand;

    public string AllocationText => Allocations.Count == 0 ? ""
        : string.Join("; ", [.. Allocations.Select(a => $"{billNumbers.GetValueOrDefault((a.BillSource, a.BillId), "")} {IndianNumberFormat.Format(new Money(a.Paise))}"),
            Format(Text.OnAccountLower, IndianNumberFormat.Format(TotalsView.Total - new Money(Allocations.Sum(a => a.Paise))))]);

    public bool IsNote => Code is "CRNT" or "DBNT";

    public bool IsOutward => Direction == Issued;

    /// <summary>An unregistered company keeps books with GST hidden (spec 13); its lines go at rate 0.</summary>
    public bool ShowsTax => Ctx.Lookups.Profile?.Registration != nameof(GstRegistration.Unregistered);

    public bool ShowsKind => IsOutward && ShowsTax;

    public bool ShowsReverseCharge => !IsOutward && ShowsTax;

    public bool ShowsPartyDoc => !IsOutward;

    public bool ShowsExport => IsOutward && Kind is nameof(InvoiceKind.ExportWithTax) or nameof(InvoiceKind.ExportUnderLut);

    public bool ShowsShipTo => IsOutward;

    public bool ShowsCountry => ShowsExport;

    TradeDirection TradeDirection => IsOutward ? TradeDirection.Outward : TradeDirection.Inward;

    bool RoundOff => !Ctx.Lookups.CompanySettings.TryGetValue("invoice.roundOff", out var json) || json.Trim() != "false";

    bool PricesIncludeTax => Ctx.Lookups.CompanySettings.TryGetValue("invoice.pricesIncludeTax", out var json) && json.Trim() == "true";

    public override object AddLine()
    {
        var line = NewLine();
        var at = CurrentLine is InvoiceLine current ? Lines.IndexOf(current) + 1 : Lines.Count;
        Lines.Insert(at, line);
        CurrentLine = line;
        return line;
    }

    protected override void RemoveLine(object line)
    {
        if (line is InvoiceLine l) Lines.Remove(l);
        if (Lines.Count == 0) Lines.Add(NewLine());
        Changed();
    }

    InvoiceLine NewLine()
    {
        var line = new InvoiceLine(() => Ctx.Lookups.Items, () => Ctx.Lookups.Ledgers(IsOutward ? LedgerUse.SalesLine : LedgerUse.PurchaseLine),
            IsOutward ? "Sales" : "Purchases", PricesIncludeTax);
        line.Changed += Changed;
        return line;
    }

    protected override void Clear()
    {
        Party.Set(null);
        PlaceOfSupply = StateOption.All.FirstOrDefault(s => s.Code == Ctx.Lookups.Profile?.State);
        Kind = nameof(InvoiceKind.Regular);
        ReverseCharge = false;
        (PartyDocNumber, PartyDocDate, OriginalNumber, OriginalDate, PortCode, ShippingBillNumber, ShippingBillDate) = ("", "", "", "", "", "", "");
        (ShipToName, ShipToAddress, ShipToState, ShipToCountry) = ("", "", null, "");
        Lines.Clear();
        Lines.Add(NewLine());
        Allocations = [];
        allocationsByHand = false;
        CurrentLine = null;
        Recalculate();
    }

    void Changed()
    {
        Recalculate();
        Edited();
    }

    partial void OnPlaceOfSupplyChanged(StateOption? value) => Changed();
    partial void OnKindChanged(string value) => Changed();
    partial void OnReverseChargeChanged(bool value) => Changed();
    partial void OnPartyDocNumberChanged(string value) => Edited();
    partial void OnOriginalNumberChanged(string value) => Edited();
    partial void OnShipToNameChanged(string value) => Edited();
    partial void OnShipToAddressChanged(string value) => Edited();
    partial void OnShipToStateChanged(StateOption? value) => Edited();
    partial void OnShipToCountryChanged(string value) => Edited();

    partial void OnDirectionChanged(string value)
    {
        if (!IsOutward) Kind = nameof(InvoiceKind.Regular);
        Allocations = [];
        Changed();
    }

    // Live totals (spec 6.4): only the taxes in use are shown.
    void Recalculate()
    {
        var draft = Draft(out _, quiet: true);
        var partyState = Party.Value is { } p && Ctx.Lookups.Parties.TryGetValue(p.Id, out var party) ? party.State : Ctx.Lookups.Profile?.State ?? "27";
        TotalsView = draft is null || Ctx.Lookups.Profile is not { } profile
            ? InvoiceTotalsView.Empty
            : InvoiceTotals.Calculate(profile, new StateCode(partyState.Length == 2 ? partyState : "27"), draft, RoundOff);
        var parts = new List<string> { Format(Text.TaxableTotal, IndianNumberFormat.Format(TotalsView.Taxable)) };
        void Add(string label, Money m)
        {
            if (m != Money.Zero) parts.Add($"{label} {IndianNumberFormat.Format(m)}");
        }
        Add(Text.CgstShort, TotalsView.Cgst);
        Add(Text.SgstShort, TotalsView.Sgst);
        Add(Text.UtgstShort, TotalsView.Utgst);
        Add(Text.IgstShort, TotalsView.Igst);
        Add(Text.CessShort, TotalsView.Cess);
        Add(Text.RoundOffShort, TotalsView.RoundOff);
        parts.Add(Format(Text.TotalLine, IndianNumberFormat.Format(TotalsView.Total)));
        Totals = string.Join("  ", parts);
        Difference = TotalsView.Problem ?? "";
        OnPropertyChanged(nameof(AllocationText));
    }

    // ---- Bills -------------------------------------------------------------------------------------------------

    // Outward: an invoice or debit note debits the party, a credit note credits it. Inward is the mirror.
    Money PartyPosting()
    {
        var raises = Code != "CRNT";
        var debit = IsOutward == raises;
        return debit ? TotalsView.Total : -TotalsView.Total;
    }

    bool Settles => IsNote && Party.Value is { } p && TotalsView.Total > Money.Zero
        && !PartySides.IsBill(p.Nature == "Liability" ? PartySide.Payable : PartySide.Receivable, PartyPosting());

    protected override bool CanAllocate => Settles;

    /// <summary>For a note that settles bills: the oldest first suggestion with the original invoice first, from one call.</summary>
    public async Task SuggestBillsAsync()
    {
        if (allocationsByHand) return;
        if (!Settles)
        {
            Allocations = [];
            return;
        }
        await RunAsync(async () =>
        {
            var (panel, first) = await PanelAsync();
            panel.SuggestOldestFirst(first);
            Allocations = panel.Result();
            Edited();
        });
    }

    protected override async Task OpenBillsAsync()
    {
        if (!Settles) return;
        Bills = (await PanelAsync()).Panel;
    }

    protected override void CloseBills()
    {
        if (Bills is { HasUnreadable: true }) return;
        if (Bills is { } panel)
        {
            Allocations = panel.Result();
            allocationsByHand = true;
            Edited();
        }
        Bills = null;
    }

    async Task<(BillsPanelViewModel Panel, BillRef? First)> PanelAsync()
    {
        var party = Party.Value!;
        var bills = await Ctx.Host.CallAsync((h, o) => h.Vouchers.OpenBillsAsync(new OpenBillsRequest
        {
            Company = Ctx.Company!.Id, Party = party.Id, ExcludingVoucher = Existing?.Id ?? 0,
        }, o));
        billNumbers = bills.Bills.ToDictionary(b => (b.Source, b.Id), b => b.Number);
        // The original invoice is this business's number for an issued note, and the supplier's own number for a received one.
        var original = OriginalNumber.Trim();
        var match = original.Length == 0 ? null : bills.Bills.FirstOrDefault(b =>
            string.Equals((IsOutward ? b.Number : b.PartyDocNumber).Trim(), original, StringComparison.OrdinalIgnoreCase));
        var panel = new BillsPanelViewModel(party.Name, party.Id, TotalsView.Total, [.. bills.Bills], Allocations, Ctx.Settings.Current.Text("ui.dateFormat"));
        return (panel, match is null ? null : BillsPanelViewModel.Ref(match));
    }

    // ---- A new party from inside the invoice (spec 6.4) --------------------------------------------------------

    public Task CreatePartyAsync()
    {
        var side = IsOutward ? PartySide.Receivable : PartySide.Payable;
        var sub = PartiesViewModel.ForNew(Ctx, Party.Text.Trim(), side, async id =>
        {
            await Ctx.Lookups.RefreshAsync();
            Party.Set(Ctx.Lookups.Ledgers(LedgerUse.Party).FirstOrDefault(l => l.Id == id));
            SubScreen = null;
        }, () => SubScreen = null);
        SubScreen = sub;
        return sub.LoadAsync(CancellationToken.None);
    }

    // ---- Saving ------------------------------------------------------------------------------------------------

    // Owner decision 1: applied automatically, and again when the total changed after the suggestion.
    async Task SuggestIfStaleAsync()
    {
        if (!allocationsByHand && Settles && new Money(Allocations.Sum(a => a.Paise)) != TotalsView.Total) await SuggestBillsAsync();
    }

    protected override async Task<SavedReply?> SaveNewAsync(DateOnly date)
    {
        await SuggestIfStaleAsync();
        if (Build(date) is not { } invoice) return null;
        invoice.RequestId = RequestId;
        return await Ctx.Host.CallAsync((h, o) => h.Vouchers.SaveInvoiceAsync(new InvoiceRequest { Company = Ctx.Company!.Id, Invoice = invoice }, o));
    }

    protected override async Task<SavedReply?> SaveEditAsync(DateOnly date, VoucherMsg existing)
    {
        await SuggestIfStaleAsync();
        if (Build(date) is not { } invoice) return null;
        return await Ctx.Host.CallAsync((h, o) => h.Vouchers.EditInvoiceAsync(new EditInvoiceRequest
        {
            Company = Ctx.Company!.Id, Id = existing.Id, RowVersion = existing.RowVersion, Invoice = invoice,
        }, o));
    }

    InvoiceMsg? Build(DateOnly date)
    {
        if (Party.Value is null && !Party.Commit() || Party.Value is null)
        {
            Message = Text.ChooseParty;
            return null;
        }
        var draft = Draft(out var error, quiet: false);
        if (draft is null)
        {
            Message = error;
            return null;
        }
        draft.Date = Wire(date);
        draft.Narration = Narration.Trim();
        foreach (var (text, set) in new (string Text, Action<string> Set)[]
                 {
                     (PartyDocDate, v => draft.PartyDocDate = v), (OriginalDate, v => draft.OriginalDate = v), (ShippingBillDate, v => draft.ShippingBillDate = v),
                 })
        {
            if (text.Trim().Length == 0) continue;
            if (!DateInput.TryParse(text, Ctx.WorkingDate, out var d))
            {
                Message = Text.VoucherDateInvalid;
                return null;
            }
            set(Wire(d));
        }
        draft.Allocations.AddRange(Allocations);
        return draft;
    }

    // The invoice as typed, in wire form. Blank lines are left out; quiet skips lines that cannot be read yet (live totals).
    InvoiceMsg? Draft(out string? error, bool quiet)
    {
        error = null;
        var msg = new InvoiceMsg
        {
            Type = VoucherType, Party = Party.Value?.Id ?? 0, Direction = TradeDirection.ToString(), Kind = IsOutward ? Kind : nameof(InvoiceKind.Regular),
            PlaceOfSupply = PlaceOfSupply?.Code ?? "", ReverseCharge = ShowsReverseCharge && ReverseCharge,
            PartyDocNumber = ShowsPartyDoc ? PartyDocNumber.Trim() : "", OriginalNumber = IsNote ? OriginalNumber.Trim() : "",
            PortCode = ShowsExport ? PortCode.Trim() : "", ShippingBillNumber = ShowsExport ? ShippingBillNumber.Trim() : "",
            ShipTo = ShowsShipTo ? new ShipToMsg
            {
                Name = ShipToName.Trim(), Address = ShipToAddress.Trim(), State = ShipToState?.Code ?? "", Country = ShowsCountry ? ShipToCountry.Trim() : "",
            } : null,
        };
        var n = 0;
        foreach (var line in Lines)
        {
            n++;
            if (line.IsBlank) continue;
            var (m, problem) = line.ToMsg(n, rateZero: !ShowsTax, commit: !quiet);
            if (m is null)
            {
                if (quiet) continue;
                error = problem;
                return null;
            }
            msg.Lines.Add(m);
        }
        return msg;
    }

    static readonly GstData Gst = GstData.LoadEmbedded();

    /// <summary>Documents this business issues print as GST documents; received ones print as a slip.</summary>
    protected override async Task<PrintResult> BuildPrintAsync(VoucherMsg stored, PrintTarget target)
    {
        if (stored.Invoice.Direction != nameof(TradeDirection.Outward))
        {
            var inv = stored.Invoice;
            var title = Code switch { "CRNT" => Text.SlipCreditNoteReceived, "DBNT" => Text.SlipDebitNoteReceived, _ => Text.SlipPurchase };
            var document = inv.PartyDocNumber.Length == 0 ? null : Format(Text.PrintSupplierDocument, inv.PartyDocNumber, VoucherSlip.Show(inv.PartyDocDate));
            return Slip(stored, title, document, receiverSigns: false);
        }
        var settings = Ctx.Settings.Current;
        var input = InvoicePrintInput.From(stored, Ctx.Lookups, settings, await Ctx.Lookups.LogoAsync());
        if (InvoicePrint.Check(input, Gst) is { } missing) return PrintResult.Refused(missing);
        IReadOnlyList<string> copies = target == PrintTarget.Pdf ? [InvoicePrint.OriginalOnly]
            : InvoiceWording.CopyLabels(InvoicePrint.AnyGoods(stored), settings.Text("invoice.copies"));
        return new PrintResult(InvoicePrint.Build(input, copies, Gst, PdfMeasure.Instance), PdfName(stored), null);
    }

    protected override async Task FillAsync(VoucherMsg v)
    {
        var inv = v.Invoice;
        Party.Set(Ctx.Lookups.Ledgers(LedgerUse.Party).FirstOrDefault(l => l.Id == inv.Party));
        Direction = inv.Direction == nameof(TradeDirection.Outward) ? Issued : Received;
        Kind = inv.Kind;
        PlaceOfSupply = StateOption.All.FirstOrDefault(s => s.Code == inv.PlaceOfSupply);
        ReverseCharge = inv.ReverseCharge;
        PartyDocNumber = inv.PartyDocNumber;
        PartyDocDate = Show(inv.PartyDocDate);
        OriginalNumber = inv.OriginalNumber;
        OriginalDate = Show(inv.OriginalDate);
        PortCode = inv.PortCode;
        ShippingBillNumber = inv.ShippingBillNumber;
        ShippingBillDate = Show(inv.ShippingBillDate);
        ShipToName = inv.ShipTo?.Name ?? "";
        ShipToAddress = inv.ShipTo?.Address ?? "";
        ShipToState = StateOption.All.FirstOrDefault(s => s.Code == inv.ShipTo?.State);
        ShipToCountry = inv.ShipTo?.Country ?? "";
        Lines.Clear();
        foreach (var stored in inv.Lines)
        {
            var line = NewLine();
            line.Fill(stored.Entry, Ctx.Lookups.Items, Ctx.Lookups.Ledgers(LedgerUse.Any));
            Lines.Add(line);
        }
        if (Lines.Count == 0) Lines.Add(NewLine());
        Recalculate();
        Allocations = [.. v.Allocations];
        allocationsByHand = Allocations.Count > 0;
        if (Allocations.Count > 0 && Party.Value is { } p)
        {
            var bills = await Ctx.Host.CallAsync((h, o) => h.Vouchers.OpenBillsAsync(new OpenBillsRequest { Company = Ctx.Company!.Id, Party = p.Id, ExcludingVoucher = v.Id }, o));
            billNumbers = bills.Bills.ToDictionary(b => (b.Source, b.Id), b => b.Number);
            OnPropertyChanged(nameof(AllocationText));
        }
    }

    string Show(string wire) => wire.Length == 0 ? "" : ShowDate(DateOnly.ParseExact(wire, "yyyy-MM-dd", CultureInfo.InvariantCulture));
}
