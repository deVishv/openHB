using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>A line of the chart: a group or a ledger, indented by depth. Opening reads like "12,000.00 Dr", or is empty.</summary>
public sealed record ChartRow(int Id, bool IsGroup, int Depth, string Name, string Opening, string Required);

/// <summary>
/// ACCT: the chart of accounts with opening balances (spec 6.3, Masters; spec 13, Opening balances). Selecting a row loads it
/// into the form; record.new starts a new ledger under the selected group; Ctrl+S saves. Loads from the lookup cache only.
/// </summary>
public sealed partial class AccountsViewModel : ScreenViewModel, IScreenCommands
{
    string? query;
    bool editingExisting;

    AccountsViewModel(ShellContext ctx, string? query) : base(ctx)
    {
        this.query = query;
        FormParent = new LookupField<GroupMsg>(() => [.. Ctx.Lookups.Chart.Groups], g => g.Name);
        Ctx.Lookups.Changed += Rebuild;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) =>
        new AccountsViewModel(ctx, cmd.Args.Count == 0 ? null : string.Join(' ', cmd.Args));

    public override string Code => "ACCT";

    public override bool HasUnsavedEdits => !editingExisting && FormName.Trim().Length > 0;

    [ObservableProperty]
    public partial IReadOnlyList<ChartRow> Rows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditOpenings))]
    public partial ChartRow? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditOpenings))]
    public partial bool FormIsGroup { get; set; }

    [ObservableProperty]
    public partial string FormName { get; set; } = "";

    public LookupField<GroupMsg> FormParent { get; }

    [ObservableProperty]
    public partial string FormOpening { get; set; } = "";

    /// <summary>"Dr" or "Cr".</summary>
    [ObservableProperty]
    public partial string FormSide { get; set; } = "Dr";

    public IReadOnlyList<string> Sides { get; } = ["Dr", "Cr"];

    /// <summary>"Opening difference 1,000.00 Dr" while the openings do not balance, else "".</summary>
    [ObservableProperty]
    public partial string OpeningDifference { get; private set; } = "";

    /// <summary>Openings need the right to edit them, and only balance sheet ledgers have one (spec 5.2, invariant 6).</summary>
    public bool CanEditOpenings =>
        Ctx.Has("EditOpeningsAndRequiredLedgers") && !FormIsGroup
        && (Selected is not { IsGroup: false } row || Nature(row.Id) is not ("Income" or "Expense"));

    public override Task LoadAsync(CancellationToken ct)
    {
        Rebuild();
        if (query is { } q)
        {
            query = null;
            var ledgers = Ctx.Lookups.Chart.Ledgers.Select(l => l.Name).ToList();
            var match = NameMatcher.Match(q, ledgers);
            if (match.Kind is not (MatchKind.None or MatchKind.Ambiguous))
                Selected = Rows.FirstOrDefault(r => !r.IsGroup && r.Name == match.Candidates[0]);
        }
        return Task.CompletedTask;
    }

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind == "Masters";

    public bool CanRun(string command) => command switch
    {
        "record.new" => true,
        "record.delete" => Selected is { IsGroup: false } && editingExisting,
        _ => false,
    };

    public async Task RunAsync(string command)
    {
        switch (command)
        {
            case "record.new":
                StartNew();
                break;
            case "record.delete" when Selected is { IsGroup: false } row:
                if (!await Ctx.Confirm.AskAsync(Format(Text.DeleteLedgerQuestion, row.Name))) return;
                await RunAsync(async () => await Ctx.Host.CallAsync((h, o) => h.Masters.RemoveLedgerAsync(new LedgerRef { Company = CompanyId, Ledger = row.Id }, o)));
                break;
        }
    }

    /// <summary>Ctrl+S: a new group or ledger, or the selected one's new name and opening.</summary>
    public override async Task SaveAsync()
    {
        Message = null;
        var name = FormName.Trim();
        if (name.Length == 0)
        {
            Message = Text.EnterName;
            return;
        }
        Money opening = Money.Zero;
        if (!FormIsGroup && FormOpening.Trim().Length > 0)
        {
            if (!Money.TryParse(FormOpening, out var typed))
            {
                Message = Text.AmountInvalid;
                return;
            }
            opening = FormSide == "Cr" ? -typed : typed;
        }
        var company = CompanyId;
        if (!editingExisting)
        {
            if (FormParent.Value is null && !FormParent.Commit() || FormParent.Value is not { } parent)
            {
                Message = Text.ChooseGroup;
                return;
            }
            await RunAsync(async () =>
            {
                if (FormIsGroup)
                {
                    await Ctx.Host.CallAsync((h, o) => h.Masters.AddGroupAsync(new AddGroupRequest { Company = company, Name = name, Parent = parent.Id }, o));
                    StartNew();
                    return;
                }
                var id = (int)(await Ctx.Host.CallAsync((h, o) => h.Masters.AddLedgerAsync(new AddLedgerRequest { Company = company, Name = name, Group = parent.Id }, o))).Id;
                // The ledger exists now; a refused opening leaves it in the form to fix.
                editingExisting = true;
                pendingSelect = id;
                if (opening != Money.Zero)
                    await Ctx.Host.CallAsync((h, o) => h.Masters.SetOpeningAsync(new SetOpeningRequest { Company = company, Ledger = id, Paise = opening.Paise }, o));
            });
            return;
        }

        if (Selected is not { } row) return;
        await RunAsync(async () =>
        {
            if (name != row.Name)
            {
                var rename = new RenameRequest { Company = company, Id = row.Id, Name = name };
                if (row.IsGroup) await Ctx.Host.CallAsync((h, o) => h.Masters.RenameGroupAsync(rename, o));
                else await Ctx.Host.CallAsync((h, o) => h.Masters.RenameLedgerAsync(rename, o));
            }
            if (!row.IsGroup && opening.Paise != OpeningOf(row.Id))
                await Ctx.Host.CallAsync((h, o) => h.Masters.SetOpeningAsync(new SetOpeningRequest { Company = company, Ledger = row.Id, Paise = opening.Paise }, o));
        });
    }

    int? pendingSelect;

    int CompanyId => Ctx.Company?.Id ?? throw new InvalidOperationException("No company is open.");

    void StartNew()
    {
        editingExisting = false;
        var group = Selected switch
        {
            { IsGroup: true } g => Ctx.Lookups.Chart.Groups.FirstOrDefault(x => x.Id == g.Id),
            { } l => Ctx.Lookups.Chart.Groups.FirstOrDefault(x => x.Id == Ctx.Lookups.Chart.Ledgers.FirstOrDefault(y => y.Id == l.Id)?.Group),
            _ => null,
        };
        FormIsGroup = false;
        FormName = "";
        FormOpening = "";
        FormSide = "Dr";
        FormParent.Set(group);
        OnPropertyChanged(nameof(HasUnsavedEdits));
    }

    partial void OnSelectedChanged(ChartRow? value)
    {
        if (value is null) return;
        editingExisting = true;
        FormIsGroup = value.IsGroup;
        FormName = value.Name;
        var chart = Ctx.Lookups.Chart;
        var parent = value.IsGroup
            ? chart.Groups.FirstOrDefault(g => g.Id == chart.Groups.First(x => x.Id == value.Id).Parent)
            : chart.Groups.FirstOrDefault(g => g.Id == chart.Ledgers.First(x => x.Id == value.Id).Group);
        FormParent.Set(parent);
        var opening = value.IsGroup ? 0 : OpeningOf(value.Id);
        FormOpening = opening == 0 ? "" : IndianNumberFormat.Format(new Money(Math.Abs(opening)));
        FormSide = opening < 0 ? "Cr" : "Dr";
    }

    partial void OnFormNameChanged(string value) => OnPropertyChanged(nameof(HasUnsavedEdits));

    long OpeningOf(int ledger) => Ctx.Lookups.Chart.Ledgers.FirstOrDefault(l => l.Id == ledger)?.OpeningPaise ?? 0;

    string Nature(int ledger)
    {
        var chart = Ctx.Lookups.Chart;
        var group = chart.Ledgers.FirstOrDefault(l => l.Id == ledger)?.Group;
        return chart.Groups.FirstOrDefault(g => g.Id == group)?.Nature ?? "";
    }

    // The chart in tree order: each group, then its ledgers, then its sub groups.
    void Rebuild()
    {
        var chart = Ctx.Lookups.Chart;
        var rows = new List<ChartRow>();
        var children = chart.Groups.ToLookup(g => g.Parent);
        var ledgers = chart.Ledgers.ToLookup(l => l.Group);
        void Add(GroupMsg g, int depth)
        {
            rows.Add(new ChartRow(g.Id, true, depth, g.Name, "", ""));
            foreach (var l in ledgers[g.Id].OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                rows.Add(new ChartRow(l.Id, false, depth + 1, l.Name,
                    l.OpeningPaise == 0 ? "" : IndianNumberFormat.FormatDrCr(new Money(l.OpeningPaise)),
                    l.Required.Length == 0 ? "" : Text.RequiredMarker));
            foreach (var c in children[g.Id].OrderBy(c => c.Id)) Add(c, depth + 1);
        }
        foreach (var top in children[0].OrderBy(g => g.Id)) Add(top, 0);
        var selected = pendingSelect ?? Selected?.Id;
        var selectedIsGroup = pendingSelect is null && Selected?.IsGroup == true;
        pendingSelect = null;
        Rows = rows;
        var difference = new Money(chart.Ledgers.Sum(l => l.OpeningPaise));
        OpeningDifference = difference == Money.Zero ? "" : Format(Text.OpeningDifference, IndianNumberFormat.FormatDrCr(difference));
        if (selected is { } id && rows.FirstOrDefault(r => r.Id == id && r.IsGroup == selectedIsGroup) is { } again)
        {
            // Keep what the user is typing; only move the selection to the fresh row.
            var (name, opening, side) = (FormName, FormOpening, FormSide);
            Selected = again;
            if (editingExisting && Message is not null) (FormName, FormOpening, FormSide) = (name, opening, side);
        }
    }

    static string Format(string format, string arg) => string.Format(CultureInfo.InvariantCulture, format, arg);
}
