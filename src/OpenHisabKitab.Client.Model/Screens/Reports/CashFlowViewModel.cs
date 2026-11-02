using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// CFLO [period]: the cash flow statement by the indirect method (spec 6.3). A group change drills to TRLB for that group,
/// net profit to PNLS, and the cash lines to TRLB for the heading the cash groups sit under.
/// </summary>
public sealed class CashFlowViewModel : ReportScreen
{
    readonly DateRange period;
    readonly string? error;

    CashFlowViewModel(ShellContext ctx, DateRange period, string? error) : base(ctx)
    {
        this.period = period;
        this.error = error;
        Columns = [new(Text.ParticularsColumn, CellAlign.Left), new(Text.AmountColumn, CellAlign.Right)];
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        var (name, period, error) = ReportArgs.NameAndPeriod(cmd.Args, ctx.WorkingDate);
        if (error is null && name.Length > 0) error = Text.PeriodNotUnderstood;
        return new CashFlowViewModel(ctx, period ?? ReportArgs.DefaultPeriod(ctx), error);
    }

    public override string Code => "CFLO";

    public override string ExportName => $"CFLO {Iso(period.From)} {Iso(period.To)}.csv";

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = error;
        if (error is not null) return Task.CompletedTask;
        Heading = Format(Text.CashFlowHeading, Show(period.From), Show(period.To));
        return RunAsync(async () =>
        {
            var cf = await Ctx.Host.CallAsync((h, o) => h.Reports.CashFlowAsync(
                new PeriodRequest { Company = Ctx.Company!.Id, Period = Wire(period) }, o), ct);
            Replace(Build(cf));
        });
    }

    List<ReportLine> Build(CashFlowMsg cf)
    {
        var groups = Ctx.Lookups.Chart.Groups.ToDictionary(g => g.Id);
        var cashHeading = groups.Values.FirstOrDefault(g => g.Kind == "CashInHand") is { } cash && groups.TryGetValue(cash.Parent, out var h)
            ? ReportArgs.Command("TRLB", h.Name, period) : null;
        var lines = new List<ReportLine>();
        foreach (var (section, title, total) in new[]
                 {
                     (nameof(CashFlowSection.Operating), Text.OperatingSection, cf.Operating),
                     (nameof(CashFlowSection.Investing), Text.InvestingSection, cf.Investing),
                     (nameof(CashFlowSection.Financing), Text.FinancingSection, cf.Financing),
                 })
        {
            lines.Add(new ReportLine(0, [Label(title), Blank], null, true));
            foreach (var l in cf.Lines.Where(l => l.Section == section))
            {
                var name = groups.TryGetValue(l.Group, out var g) ? g.Name : "";
                var (label, drill) = l.Item switch
                {
                    nameof(CashFlowItem.NetProfit) => (Text.NetProfitLine, ReportArgs.Command("PNLS", "", period)),
                    nameof(CashFlowItem.Depreciation) => (Text.AddDepreciationLine, (string?)null),
                    _ => (Format(Text.ChangeInGroupLine, name), ReportArgs.Command("TRLB", name, period)),
                };
                lines.Add(new ReportLine(1, [Label(label), Amount(new Money(l.Amount))], drill, false));
            }
            lines.Add(new ReportLine(0, [Label(Text.ReportTotal), Amount(new Money(total))], null, true));
        }
        lines.Add(new ReportLine(0, [Label(Text.NetChangeInCashLine), Amount(new Money(cf.NetChange))], null, true));
        lines.Add(new ReportLine(0, [Label(Text.OpeningCashLine), Amount(new Money(cf.OpeningCash))], cashHeading, false));
        lines.Add(new ReportLine(0, [Label(Text.ClosingCashLine), Amount(new Money(cf.ClosingCash))], cashHeading, true));
        return lines;
    }
}
