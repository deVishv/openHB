using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// DAYB [period]: vouchers by date (spec 6.3), the working date alone by default, 500 to a page. The host lists only a
/// Data entry user's own vouchers (spec 8). A line opens its voucher.
/// </summary>
public sealed class DayBookViewModel : ReportScreen
{
    const int PageSize = 500;
    readonly DateRange period;
    readonly string? error;
    string next = "";

    DayBookViewModel(ShellContext ctx, DateRange period, string? error) : base(ctx)
    {
        this.period = period;
        this.error = error;
        Columns =
        [
            new(Text.DateColumn, CellAlign.Left), new(Text.TypeColumn, CellAlign.Left), new(Text.NumberColumn, CellAlign.Left),
            new(Text.PartyOrLedgerColumn, CellAlign.Left), new(Text.AmountColumn, CellAlign.Right), new(Text.NarrationColumn, CellAlign.Left),
            new(Text.StatusColumn, CellAlign.Left),
        ];
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        var (name, period, error) = ReportArgs.NameAndPeriod(cmd.Args, ctx.WorkingDate);
        if (error is null && name.Length > 0) error = Text.PeriodNotUnderstood;
        return new DayBookViewModel(ctx, period ?? new DateRange(ctx.WorkingDate, ctx.WorkingDate), error);
    }

    public override string Code => "DAYB";

    public override string ExportName => $"DAYB {Iso(period.From)} {Iso(period.To)}.csv";

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = error;
        if (error is not null) return Task.CompletedTask;
        Heading = Format(Text.DayBookHeading, Show(period.From), Show(period.To));
        return RunAsync(async () =>
        {
            next = "";
            Replace(await Page(ct));
        });
    }

    public override Task MoreAsync() => HasMore ? RunAsync(async () =>
    {
        foreach (var l in await Page(CancellationToken.None)) Lines.Add(l);
    }) : Task.CompletedTask;

    async Task<List<ReportLine>> Page(CancellationToken ct)
    {
        var page = await Ctx.Host.CallAsync((h, o) => h.Vouchers.ListAsync(new ListVouchersRequest
        {
            Company = Ctx.Company!.Id, Period = Wire(period), Page = new PageMsg { Limit = PageSize, After = next },
        }, o), ct);
        next = page.Next;
        HasMore = next.Length > 0;
        return [.. page.Vouchers.Select(v =>
        {
            var code = VoucherCodes.CodeOf(v.Type);
            return new ReportLine(0,
            [
                Label(Show(FromIso(v.Date))), Label(code), Label(v.DisplayNumber), Label(VoucherRowText.Party(v, Ctx.Lookups)),
                v.Cancelled ? Blank : Amount(VoucherRowText.DebitTotal(v)), Label(v.Narration), Label(v.Cancelled ? Text.CancelledMarker : ""),
            ], $"{code} {v.DisplayNumber}", false);
        })];
    }
}
