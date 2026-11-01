using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>A ledger line as typed. JRNL and CNTR use Debit and Credit; RCPT and PYMT use Amount.</summary>
public sealed partial class EntryLine : ObservableObject
{
    public EntryLine(Func<IReadOnlyList<LedgerOption>> ledgers)
    {
        Ledger = new LookupField<LedgerOption>(ledgers, l => l.Name);
        Ledger.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(LookupField<LedgerOption>.Value)) return;
            // Another party's bills never stay on the line.
            Allocations = [];
            Settles = Money.Zero;
            ChosenByHand = false;
            Changed?.Invoke();
        };
    }

    public LookupField<LedgerOption> Ledger { get; }

    [ObservableProperty]
    public partial string Debit { get; set; } = "";

    [ObservableProperty]
    public partial string Credit { get; set; } = "";

    [ObservableProperty]
    public partial string Amount { get; set; } = "";

    /// <summary>The bills this line's party settles; what is not allocated stays on account.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllocationText))]
    public partial IReadOnlyList<AllocationMsg> Allocations { get; set; } = [];

    /// <summary>Bill numbers by id, for AllocationText.</summary>
    internal IReadOnlyDictionary<(string Source, long Id), string> BillNumbers { get; set; } = new Dictionary<(string, long), string>();

    /// <summary>True once the user changed the allocation in the bills panel, or for a stored voucher's own; the suggestion then leaves it alone.</summary>
    internal bool ChosenByHand { get; set; }

    /// <summary>The settlement the allocations were made for, for the on account amount.</summary>
    internal Money Settles { get; set; }

    /// <summary>"INV/26-27/00001 5,000.00; on account 0.00", or "" when nothing is allocated.</summary>
    public string AllocationText
    {
        get
        {
            if (Allocations.Count == 0) return "";
            var parts = Allocations.Select(a => $"{BillNumbers.GetValueOrDefault((a.BillSource, a.BillId), "")} {IndianNumberFormat.Format(new Money(a.Paise))}");
            var onAccount = Settles - new Money(Allocations.Sum(a => a.Paise));
            return string.Join("; ", [.. parts, string.Format(CultureInfo.InvariantCulture, Text.OnAccountLower, IndianNumberFormat.Format(onAccount))]);
        }
    }

    public bool IsBlank => Ledger.Value is null && Ledger.Text.Trim().Length == 0 && Debit.Trim().Length == 0 && Credit.Trim().Length == 0 && Amount.Trim().Length == 0;

    /// <summary>Raised when anything on the line changes, for live totals.</summary>
    public event Action? Changed;

    partial void OnDebitChanged(string value) => Changed?.Invoke();

    partial void OnCreditChanged(string value) => Changed?.Invoke();

    partial void OnAmountChanged(string value)
    {
        Changed?.Invoke();
    }
}
