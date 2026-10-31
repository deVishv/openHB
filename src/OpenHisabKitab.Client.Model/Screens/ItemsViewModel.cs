using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>A GST rate choice: 1800 basis points shows as "18%".</summary>
public sealed record RateOption(int BasisPoints, string Label);

/// <summary>
/// ITEM: goods and services with HSN or SAC, GST rate, unit, and default price (spec 6.3, Masters). A list and a form;
/// record.new starts a new item, Ctrl+S saves, Ctrl+F filters. Loads from the lookup cache only.
/// </summary>
public sealed partial class ItemsViewModel : ScreenViewModel, IScreenCommands
{
    static readonly GstData Gst = GstData.LoadEmbedded();
    string? query;
    ItemMsg? editing;

    ItemsViewModel(ShellContext ctx, string? query) : base(ctx)
    {
        this.query = query;
        Rates = [.. Gst.RatesOn(ctx.WorkingDate).Select(r => new RateOption(r.BasisPoints, EntryNumbers.Rate(r.BasisPoints) + "%"))];
        FormRate = Rates.FirstOrDefault(r => r.BasisPoints == 1800) ?? Rates[0];
        Ctx.Lookups.Changed += Refresh;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) =>
        new ItemsViewModel(ctx, cmd.Args.Count == 0 ? null : string.Join(' ', cmd.Args));

    public override string Code => "ITEM";

    public override bool HasUnsavedEdits => editing is null && FormName.Trim().Length > 0;

    /// <summary>Rates valid on the working date (Core GstData).</summary>
    public IReadOnlyList<RateOption> Rates { get; }

    [ObservableProperty]
    public partial IReadOnlyList<ItemMsg> Items { get; private set; } = [];

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial ItemMsg? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial string FormName { get; set; } = "";

    [ObservableProperty]
    public partial string FormHsnSac { get; set; } = "";

    [ObservableProperty]
    public partial bool FormIsService { get; set; }

    [ObservableProperty]
    public partial string FormUnit { get; set; } = "";

    [ObservableProperty]
    public partial RateOption FormRate { get; set; }

    [ObservableProperty]
    public partial string FormCessRate { get; set; } = "";

    [ObservableProperty]
    public partial string FormDefaultPrice { get; set; } = "";

    /// <summary>The view focuses the filter when this is raised (Ctrl+F).</summary>
    public event Action? FindRequested;

    public override void Find() => FindRequested?.Invoke();

    public override Task LoadAsync(CancellationToken ct)
    {
        Refresh();
        if (query is { } q)
        {
            query = null;
            var match = NameMatcher.Match(q, [.. Items.Select(i => i.Name)]);
            if (match.Kind is not (MatchKind.None or MatchKind.Ambiguous)) Selected = Items.First(i => i.Name == match.Candidates[0]);
        }
        return Task.CompletedTask;
    }

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind == "Masters";

    public bool CanRun(string command) => command == "record.new";

    public Task RunAsync(string command)
    {
        if (command == "record.new") Load(null);
        return Task.CompletedTask;
    }

    public override async Task SaveAsync()
    {
        Message = null;
        if (!Money.TryParse(FormDefaultPrice.Trim().Length == 0 ? "0" : FormDefaultPrice, out var price) || price < Money.Zero)
        {
            Message = Text.AmountInvalid;
            return;
        }
        if (!EntryNumbers.TryRate(FormCessRate, out var cess))
        {
            Message = Text.RateInvalid;
            return;
        }
        var item = new ItemMsg
        {
            Id = editing?.Id ?? 0, RowVersion = editing?.RowVersion ?? 0, Name = FormName.Trim(), HsnSac = FormHsnSac.Trim(),
            IsService = FormIsService, Unit = FormUnit.Trim(), RateBp = FormRate.BasisPoints, CessBp = cess, DefaultPricePaise = price.Paise,
        };
        var company = Ctx.Company!.Id;
        Busy = true;
        try
        {
            var id = (int)(await Ctx.Host.CallAsync((h, o) => h.Masters.SaveItemAsync(new SaveItemRequest { Company = company, Item = item }, o))).Id;
            pendingSelect = id;
            editing = item.Clone();
            editing.Id = id;
            editing.RowVersion = item.RowVersion + 1;
            OnPropertyChanged(nameof(HasUnsavedEdits));
        }
        catch (HostException e) when (e.Kind == HostErrorKind.Conflict)
        {
            // Show the current item so the user can redo the change on it.
            Load(e.Current(ItemMsg.Parser));
            Message = e.Message;
        }
        catch (HostException e) { Message = e.Message; }
        finally { Busy = false; }
    }

    int? pendingSelect;

    partial void OnSelectedChanged(ItemMsg? value)
    {
        if (value is not null) Load(value);
    }

    partial void OnFilterChanged(string value) => Refresh();

    void Load(ItemMsg? item)
    {
        editing = item;
        FormName = item?.Name ?? "";
        FormHsnSac = item?.HsnSac ?? "";
        FormIsService = item?.IsService ?? false;
        FormUnit = item?.Unit ?? "";
        FormRate = Rates.FirstOrDefault(r => r.BasisPoints == (item?.RateBp ?? 1800)) ?? new RateOption(item!.RateBp, EntryNumbers.Rate(item.RateBp) + "%");
        FormCessRate = item is null || item.CessBp == 0 ? "" : EntryNumbers.Rate(item.CessBp);
        FormDefaultPrice = item is null || item.DefaultPricePaise == 0 ? "" : IndianNumberFormat.Format(new Money(item.DefaultPricePaise));
        OnPropertyChanged(nameof(HasUnsavedEdits));
    }

    void Refresh()
    {
        var all = Ctx.Lookups.Items;
        var filter = Filter.Trim();
        Items = filter.Length == 0 ? all : [.. all.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.HsnSac.StartsWith(filter, StringComparison.Ordinal))];
        if (pendingSelect is { } id && Items.FirstOrDefault(i => i.Id == id) is { } saved)
        {
            pendingSelect = null;
            Selected = saved;
        }
        else if (editing is { Id: > 0 } e && Items.FirstOrDefault(i => i.Id == e.Id) is { } fresh && fresh.RowVersion == e.RowVersion) editing = fresh;
    }
}
