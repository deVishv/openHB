using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// PNLS [period] and BSHT [as at]: the profit and loss account and the balance sheet (spec 6.3), in Schedule III style by
/// default or by the chart of accounts (Ctrl+G), with last year's column from the second year. A group line drills to
/// TRLB for that group, a ledger to LEDG, and the profit lines to PNLS.
/// </summary>
public sealed class StatementViewModel : ReportScreen
{
    readonly bool balanceSheet;
    readonly DateRange period;
    readonly string? error;

    StatementViewModel(ShellContext ctx, bool balanceSheet, DateRange period, string? error) : base(ctx)
    {
        this.balanceSheet = balanceSheet;
        this.period = period;
        this.error = error;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        if (cmd.Code == "BSHT")
        {
            var (others, asAt, _) = ReportArgs.RestAndDate(cmd.Args, ctx.WorkingDate);
            var date = asAt ?? ReportArgs.DefaultAsAt(ctx);
            return new StatementViewModel(ctx, true, new DateRange(FinancialYear.Containing(date).Start, date),
                others.Length > 0 ? Text.DateNotUnderstood : null);
        }
        var (name, period, error) = ReportArgs.NameAndPeriod(cmd.Args, ctx.WorkingDate);
        if (error is null && name.Length > 0) error = Text.PeriodNotUnderstood;
        return new StatementViewModel(ctx, false, period ?? ReportArgs.DefaultPeriod(ctx), error);
    }

    public override string Code => balanceSheet ? "BSHT" : "PNLS";

    public LayoutStyle Style { get; private set; } = LayoutStyle.ScheduleIII;

    public override string ExportName => balanceSheet ? $"BSHT {Iso(period.To)}.csv" : $"PNLS {Iso(period.From)} {Iso(period.To)}.csv";

    /// <summary>
    /// The words for a line kind that has no chart name; the host uses the same words for REST and MCP. When this year
    /// and last year differ in sign, the result line is "Net profit / (loss)", so neither column sits under the wrong word.
    /// </summary>
    public static string Wording(string kind, long current, long? previous = null) => kind switch
    {
        nameof(LayoutLineKind.CashAndEquivalents) => Text.CashAndEquivalentsLine,
        nameof(LayoutLineKind.ProfitForPeriod) => Text.ProfitForPeriodLine,
        nameof(LayoutLineKind.OpeningDifference) => Text.ReportOpeningDifference,
        nameof(LayoutLineKind.SideTotal) => Text.ReportTotal,
        nameof(LayoutLineKind.NetProfit) when previous is { } p && (p < 0) != (current < 0) => Text.NetProfitOrLossLine,
        nameof(LayoutLineKind.NetProfit) => current < 0 ? Text.NetLossLine : Text.NetProfitLine,
        _ => "",
    };

    public override bool CanRun(string command) => command == "report.style" || base.CanRun(command);

    public override Task RunAsync(string command) => command == "report.style" ? ToggleStyleAsync() : base.RunAsync(command);

    public Task ToggleStyleAsync()
    {
        Style = Style == LayoutStyle.ScheduleIII ? LayoutStyle.Chart : LayoutStyle.ScheduleIII;
        OnPropertyChanged(nameof(Style));
        return LoadAsync(CancellationToken.None);
    }

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = error;
        if (error is not null) return Task.CompletedTask;
        var style = Style == LayoutStyle.ScheduleIII ? Text.StyleScheduleIII : Text.StyleChart;
        Heading = balanceSheet
            ? Format(Text.BalanceSheetHeading, style, Show(period.To))
            : Format(Text.ProfitAndLossHeading, style, Show(period.From), Show(period.To));
        return RunAsync(async () =>
        {
            var company = Ctx.Company!.Id;
            var wireStyle = Style.ToString();
            var layout = balanceSheet
                ? await Ctx.Host.CallAsync((h, o) => h.Reports.BalanceSheetLayoutAsync(
                    new LayoutAsAtRequest { Company = company, AsAt = Iso(period.To), Style = wireStyle }, o), ct)
                : await Ctx.Host.CallAsync((h, o) => h.Reports.ProfitAndLossLayoutAsync(
                    new LayoutPeriodRequest { Company = company, Period = Wire(period), Style = wireStyle }, o), ct);
            Columns = layout.HasPrevious
                ? [new(Text.ParticularsColumn, CellAlign.Left), new(Show(period.To), CellAlign.Right), new(Show(period.To.AddYears(-1)), CellAlign.Right)]
                : [new(Text.ParticularsColumn, CellAlign.Left), new(Show(period.To), CellAlign.Right)];
            Replace(Build(layout));
        });
    }

    List<ReportLine> Build(LayoutMsg layout)
    {
        var lines = new List<ReportLine>();
        var groups = Ctx.Lookups.Chart.Groups.ToDictionary(g => g.Id);
        string? side = null;
        foreach (var l in layout.Lines)
        {
            if (l.Side != side)
            {
                side = l.Side;
                lines.Add(Line(0, SideName(side), null, true, null));
            }
            var label = l.Kind switch
            {
                nameof(LayoutLineKind.HeadingTotal) => Format(Text.ReportTotalFor, l.Label),
                nameof(LayoutLineKind.Heading) or nameof(LayoutLineKind.Group) or nameof(LayoutLineKind.Ledger) => l.Label,
                _ => Wording(l.Kind, l.Current, layout.HasPrevious ? l.Previous : null),
            };
            string? drill = l.Kind switch
            {
                nameof(LayoutLineKind.Group) => ReportArgs.Command("TRLB", l.Label, period),
                // Cash in hand and bank accounts are one line, so it opens the heading they sit under.
                nameof(LayoutLineKind.CashAndEquivalents) when groups.TryGetValue(l.Group, out var cash) && groups.TryGetValue(cash.Parent, out var heading)
                    => ReportArgs.Command("TRLB", heading.Name, period),
                nameof(LayoutLineKind.Ledger) => ReportArgs.Command("LEDG", l.Label, period),
                nameof(LayoutLineKind.ProfitForPeriod) or nameof(LayoutLineKind.NetProfit) => ReportArgs.Command("PNLS", "", period),
                _ => null,
            };
            var emphasis = l.Kind is nameof(LayoutLineKind.Heading) or nameof(LayoutLineKind.HeadingTotal) or nameof(LayoutLineKind.SideTotal)
                or nameof(LayoutLineKind.NetProfit);
            (Money Current, Money Previous)? amounts = l.Kind == nameof(LayoutLineKind.Heading) ? null : (new Money(l.Current), new Money(l.Previous));
            lines.Add(Line(l.Depth, label, drill, emphasis, amounts));
        }
        return lines;
    }

    // One amount cell per column after Particulars: this period, and last year's when shown.
    ReportLine Line(int depth, string label, string? drill, bool emphasis, (Money Current, Money Previous)? amounts)
    {
        var cells = new List<ReportCell> { Label(label) };
        var columns = Columns.Count - 1;
        if (amounts is { } a)
        {
            cells.Add(Amount(a.Current));
            if (columns == 2) cells.Add(Amount(a.Previous));
        }
        else for (var i = 0; i < columns; i++) cells.Add(Blank);
        return new ReportLine(depth, cells, drill, emphasis);
    }

    static string SideName(string side) => side switch
    {
        nameof(LayoutSide.EquityAndLiabilities) => Text.SideEquityAndLiabilities,
        nameof(LayoutSide.Assets) => Text.SideAssets,
        nameof(LayoutSide.Income) => Text.SideIncome,
        _ => Text.SideExpenses,
    };
}
