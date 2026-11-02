using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// TRLB [group] [period]: every ledger's opening, debits, credits, and closing (spec 6.3). A group name limits it to the
/// ledgers under that group, which is how BSHT, PNLS, and CFLO lines drill down. A ledger line drills to LEDG.
/// </summary>
public sealed class TrialBalanceViewModel : ReportScreen
{
    readonly DateRange period;
    readonly string groupText;
    readonly string? error;
    GroupMsg? group;

    TrialBalanceViewModel(ShellContext ctx, string groupText, DateRange period, string? error) : base(ctx)
    {
        this.groupText = groupText;
        this.period = period;
        this.error = error;
        Columns =
        [
            new(Text.LedgerColumn, CellAlign.Left), new(Text.OpeningColumn, CellAlign.Right), new(Text.DebitColumn, CellAlign.Right),
            new(Text.CreditColumn, CellAlign.Right), new(Text.ClosingColumn, CellAlign.Right),
        ];
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        var (name, period, error) = ReportArgs.NameAndPeriod(cmd.Args, ctx.WorkingDate);
        return new TrialBalanceViewModel(ctx, name, period ?? ReportArgs.DefaultPeriod(ctx), error);
    }

    public override string Code => "TRLB";

    public override string Subject => group?.Name ?? groupText;

    public override string ExportName =>
        group is null ? $"TRLB {Iso(period.From)} {Iso(period.To)}.csv" : $"TRLB {FileSafe(group.Name)} {Iso(period.From)} {Iso(period.To)}.csv";

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = error;
        if (error is not null) return Task.CompletedTask;
        if (groupText.Length > 0 && !FindGroup()) return Task.CompletedTask;
        Heading = group is null
            ? Format(Text.TrialBalanceHeading, Show(period.From), Show(period.To))
            : Format(Text.TrialBalanceGroupHeading, group.Name, Show(period.From), Show(period.To));
        return RunAsync(async () =>
        {
            var tb = await Ctx.Host.CallAsync((h, o) => h.Reports.TrialBalanceAsync(
                new PeriodRequest { Company = Ctx.Company!.Id, Period = Wire(period) }, o), ct);
            Replace(Build(tb));
        });
    }

    bool FindGroup()
    {
        var groups = Ctx.Lookups.Chart.Groups;
        var match = NameMatcher.Match(groupText, [.. groups.Select(g => g.Name)]);
        switch (match.Kind)
        {
            case MatchKind.None:
                Message = Format(Text.NoGroupNamed, groupText);
                return false;
            case MatchKind.Ambiguous:
                Message = Format(Text.GroupNameAmbiguous, groupText);
                return false;
        }
        group = groups.First(g => g.Name == match.Candidates[0]);
        OnPropertyChanged(nameof(Subject));
        return true;
    }

    List<ReportLine> Build(TrialBalanceMsg tb)
    {
        var chart = Ctx.Lookups.Chart;
        var groupOf = chart.Ledgers.ToDictionary(l => l.Id, l => l.Group);
        var under = group is null ? null : Under(chart, group.Id);
        var rows = tb.Rows.Where(r => under is null || (groupOf.TryGetValue(r.Ledger, out var g) && under.Contains(g))).ToList();

        var lines = rows.Select(r =>
        {
            var name = Ctx.Lookups.LedgerName(r.Ledger);
            return new ReportLine(0,
                [Label(name), DrCr(new Money(r.Opening)), Amount(new Money(r.Debits)), Amount(new Money(r.Credits)), DrCr(new Money(r.Closing))],
                ReportArgs.Command("LEDG", name, period), false);
        }).ToList();

        var debits = new Money(rows.Sum(r => r.Debits));
        var credits = new Money(rows.Sum(r => r.Credits));
        if (group is null)
        {
            if (tb.OpeningDifference != 0)
            {
                var difference = -new Money(tb.OpeningDifference);
                lines.Add(new ReportLine(0, [Label(Text.ReportOpeningDifference), DrCr(difference), Blank, Blank, DrCr(difference)], null, false));
            }
            lines.Add(new ReportLine(0, [Label(Text.ReportTotal), Blank, Amount(debits), Amount(credits), Blank], null, true));
        }
        else
        {
            lines.Add(new ReportLine(0,
                [Label(Format(Text.ReportTotalFor, group.Name)), DrCr(new Money(rows.Sum(r => r.Opening))), Amount(debits), Amount(credits),
                 DrCr(new Money(rows.Sum(r => r.Closing)))], null, true));
        }
        return lines;
    }

    /// <summary>The group and every group beneath it.</summary>
    internal static HashSet<int> Under(ChartMsg chart, int group)
    {
        var set = new HashSet<int> { group };
        bool added;
        do
        {
            added = false;
            foreach (var g in chart.Groups)
                if (g.Parent != 0 && set.Contains(g.Parent) && set.Add(g.Id)) added = true;
        } while (added);
        return set;
    }
}
