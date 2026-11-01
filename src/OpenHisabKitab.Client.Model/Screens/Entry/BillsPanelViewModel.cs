using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>
/// The bills a line's party can settle (spec 13, Bill by bill settlement): each open bill with the amount to allocate. Core's
/// AllocationCheck runs on every change and its problem shows in the host's words; the host checks again on save.
/// </summary>
public sealed partial class BillsPanelViewModel : ObservableObject
{
    public sealed partial class Row(BillMsg bill, string date, Action changed) : ObservableObject
    {
        public BillMsg Bill { get; } = bill;
        public string Date { get; } = date;
        public string Open { get; } = IndianNumberFormat.Format(new Money(bill.AmountPaise - bill.SettledPaise));

        [ObservableProperty]
        public partial string Allocate { get; set; } = "";

        partial void OnAllocateChanged(string value) => changed();
    }

    readonly LedgerId party;
    readonly Money settlement;

    public BillsPanelViewModel(string partyName, int partyId, Money settlement, IReadOnlyList<BillMsg> bills, IReadOnlyList<AllocationMsg> current, string dateFormat)
    {
        PartyName = partyName;
        party = new LedgerId(partyId);
        this.settlement = settlement;
        Heading = string.Format(CultureInfo.InvariantCulture, Text.BillsHeading, partyName);
        Rows = [.. bills.Where(b => b.AmountPaise - b.SettledPaise > 0 || current.Any(a => Same(a, b)))
            .Select(b => new Row(b, DateText.Format(DateOnly.ParseExact(b.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), dateFormat), Recheck))];
        foreach (var a in current)
            if (Rows.FirstOrDefault(r => Same(a, r.Bill)) is { } row) row.Allocate = IndianNumberFormat.Format(new Money(a.Paise));
        Recheck();
    }

    public string PartyName { get; }

    public string Heading { get; }

    public IReadOnlyList<Row> Rows { get; }

    /// <summary>"On account 3,000.00": what is not allocated.</summary>
    [ObservableProperty]
    public partial string OnAccount { get; private set; } = "";

    /// <summary>True while an amount cannot be read; the panel then stays open on Esc.</summary>
    [ObservableProperty]
    public partial bool HasUnreadable { get; private set; }

    /// <summary>The first problem, in the host's words, or null.</summary>
    [ObservableProperty]
    public partial string? Problem { get; private set; }

    /// <summary>Oldest first (Core BillAllocator); the bill named by first goes first.</summary>
    public void SuggestOldestFirst(BillRef? first = null)
    {
        var suggested = BillAllocator.Suggest([.. Rows.Select(r => Open(r.Bill))], settlement, first)
            .ToDictionary(a => a.Bill, a => a.Amount);
        foreach (var row in Rows)
            row.Allocate = suggested.TryGetValue(Ref(row.Bill), out var m) ? IndianNumberFormat.Format(m) : "";
        Recheck();
    }

    public IReadOnlyList<AllocationMsg> Result() =>
        [.. Rows.Select(r => (Row: r, Ok: Money.TryParse(r.Allocate, out var m), Amount: m))
            .Where(x => x.Ok && x.Amount != Money.Zero)
            .Select(x => new AllocationMsg { Party = party.Value, BillSource = x.Row.Bill.Source, BillId = x.Row.Bill.Id, Paise = x.Amount.Paise })];

    void Recheck()
    {
        var allocations = new List<Allocation>();
        HasUnreadable = false;
        foreach (var row in Rows)
        {
            if (row.Allocate.Trim().Length == 0) continue;
            if (!Money.TryParse(row.Allocate, out var m))
            {
                HasUnreadable = true;
                Problem = Format(Text.BillAmountInvalid, row.Bill.Number);
                return;
            }
            allocations.Add(new Allocation(Ref(row.Bill), m));
        }
        var total = Money.Sum(allocations.Select(a => a.Amount));
        OnAccount = string.Format(CultureInfo.InvariantCulture, Text.OnAccountLine, IndianNumberFormat.Format(settlement - total));
        var bills = Rows.ToDictionary(r => Ref(r.Bill), r => Open(r.Bill));
        var problem = AllocationCheck.Check(party, settlement, allocations, bills);
        Problem = problem is null ? null : Words(problem, bills);
    }

    string Words(AllocationProblem p, IReadOnlyDictionary<BillRef, OpenBill> bills)
    {
        string Number() => bills[p.Bill!.Value].Number;
        return p.Error switch
        {
            AllocationError.NotPositive => Text.AllocationNotPositive,
            AllocationError.Duplicate => Format(Text.AllocationDuplicate, Number()),
            AllocationError.MoreThanOpen => Format(Text.AllocationMoreThanOpen, Number(), IndianNumberFormat.Format(bills[p.Bill!.Value].Open)),
            AllocationError.MoreThanSettlement => Format(Text.AllocationMoreThanSettlement, IndianNumberFormat.Format(settlement), PartyName),
            _ => Text.AllocationNotPositive,
        };
    }

    static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    static bool Same(AllocationMsg a, BillMsg b) => a.BillSource == b.Source && a.BillId == b.Id;

    internal static BillRef Ref(BillMsg b) => new(Enum.Parse<BillSource>(b.Source), b.Id);

    OpenBill Open(BillMsg b) =>
        new(Ref(b), new LedgerId(b.Party), b.Number, b.PartyDocNumber.Length == 0 ? null : b.PartyDocNumber,
            DateOnly.ParseExact(b.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), null, new Money(b.AmountPaise), new Money(b.SettledPaise));
}
