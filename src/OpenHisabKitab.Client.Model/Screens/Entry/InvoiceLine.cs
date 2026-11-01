using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>An invoice line as typed. Picking an item fills its description, HSN or SAC, rate, cess, price, and the Sales or Purchases ledger.</summary>
public sealed partial class InvoiceLine : ObservableObject
{
    readonly Func<IReadOnlyList<LedgerOption>> ledgers;
    readonly string defaultLedger;

    public InvoiceLine(Func<IReadOnlyList<ItemMsg>> items, Func<IReadOnlyList<LedgerOption>> ledgers, string defaultLedger, bool priceIncludesTax)
    {
        this.ledgers = ledgers;
        this.defaultLedger = defaultLedger;
        PriceIncludesTax = priceIncludesTax;
        Item = new LookupField<ItemMsg>(items, i => i.Name);
        Ledger = new LookupField<LedgerOption>(ledgers, l => l.Name);
        Item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Item.Value) && Item.Value is { } item) Take(item);
        };
        Ledger.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Ledger.Value)) Changed?.Invoke();
        };
    }

    public LookupField<ItemMsg> Item { get; }

    public LookupField<LedgerOption> Ledger { get; }

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string HsnSac { get; set; } = "";

    [ObservableProperty]
    public partial string Quantity { get; set; } = "";

    [ObservableProperty]
    public partial string Price { get; set; } = "";

    [ObservableProperty]
    public partial string Discount { get; set; } = "";

    /// <summary>GST rate as a percentage, such as "18".</summary>
    [ObservableProperty]
    public partial string Rate { get; set; } = "";

    [ObservableProperty]
    public partial string Cess { get; set; } = "";

    [ObservableProperty]
    public partial bool PriceIncludesTax { get; set; }

    public bool IsBlank => Item.Value is null && Ledger.Value is null && Description.Trim().Length == 0 && Price.Trim().Length == 0 && Quantity.Trim().Length == 0;

    public event Action? Changed;

    void Take(ItemMsg item)
    {
        Description = item.Name;
        HsnSac = item.HsnSac;
        Rate = EntryNumbers.Rate(item.RateBp);
        Cess = item.CessBp == 0 ? "" : EntryNumbers.Rate(item.CessBp);
        Price = item.DefaultPricePaise == 0 ? Price : IndianNumberFormat.Format(new Money(item.DefaultPricePaise));
        if (Quantity.Trim().Length == 0) Quantity = "1";
        if (Ledger.Value is null) Ledger.Set(ledgers().FirstOrDefault(l => l.Required == defaultLedger));
        Changed?.Invoke();
    }

    /// <summary>The line in wire form, or the reason it cannot be read. n is its 1-based number.</summary>
    /// <summary>commit false (live totals) never picks a ledger from what is typed; only a save commits the name.</summary>
    internal (InvoiceLineMsg? Line, string? Error) ToMsg(int n, bool rateZero, bool commit = true)
    {
        string Bad(string text) => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, n);
        if (Ledger.Value is null && (!commit || !Ledger.Commit()) || Ledger.Value is not { } ledger) return (null, Bad(Resources.Text.LineChooseLedger));
        if (!EntryNumbers.TryQuantity(Quantity.Trim().Length == 0 ? "1" : Quantity, out var qty)) return (null, Bad(Resources.Text.LineQuantityInvalid));
        if (!Money.TryParse(Price, out var price)) return (null, Bad(Resources.Text.LineAmountInvalid));
        var discount = Money.Zero;
        if (Discount.Trim().Length > 0 && !Money.TryParse(Discount, out discount)) return (null, Bad(Resources.Text.LineAmountInvalid));
        if (!EntryNumbers.TryRate(Rate, out var rate) || !EntryNumbers.TryRate(Cess, out var cess)) return (null, Bad(Resources.Text.LineRateInvalid));
        return (new InvoiceLineMsg
        {
            ItemId = Item.Value?.Id ?? 0, Ledger = ledger.Id, Description = Description.Trim(), HsnSac = HsnSac.Trim(), QtyThousandths = qty,
            UnitPricePaise = price.Paise, DiscountPaise = discount.Paise, RateBp = rateZero ? 0 : rate, CessBp = rateZero ? 0 : cess, PriceIncludesTax = PriceIncludesTax,
        }, null);
    }

    internal void Fill(InvoiceLineMsg m, IReadOnlyList<ItemMsg> items, IReadOnlyList<LedgerOption> all)
    {
        Item.Set(items.FirstOrDefault(i => i.Id == m.ItemId));
        Ledger.Set(all.FirstOrDefault(l => l.Id == m.Ledger));
        Description = m.Description;
        HsnSac = m.HsnSac;
        Quantity = EntryNumbers.Quantity(m.QtyThousandths);
        Price = IndianNumberFormat.Format(new Money(m.UnitPricePaise));
        Discount = m.DiscountPaise == 0 ? "" : IndianNumberFormat.Format(new Money(m.DiscountPaise));
        Rate = EntryNumbers.Rate(m.RateBp);
        Cess = m.CessBp == 0 ? "" : EntryNumbers.Rate(m.CessBp);
        PriceIncludesTax = m.PriceIncludesTax;
    }

    partial void OnDescriptionChanged(string value) => Changed?.Invoke();
    partial void OnQuantityChanged(string value) => Changed?.Invoke();
    partial void OnPriceChanged(string value) => Changed?.Invoke();
    partial void OnDiscountChanged(string value) => Changed?.Invoke();
    partial void OnRateChanged(string value) => Changed?.Invoke();
    partial void OnCessChanged(string value) => Changed?.Invoke();
    partial void OnPriceIncludesTaxChanged(bool value) => Changed?.Invoke();
}
