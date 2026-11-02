using System.Globalization;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// OUTS [receivable|payable|r|p] [party] [as at]: receivables or payables bill by bill, aged by days overdue in the
/// columns the company's outstanding.ageingDays setting gives (spec 6.3). A party drills to its bills, and a voucher bill
/// to its voucher.
/// </summary>
public sealed class OutstandingViewModel : ReportScreen
{
    readonly bool payable;
    readonly string partyText;
    readonly DateOnly asAt;
    string? partyName;

    OutstandingViewModel(ShellContext ctx, bool payable, string partyText, DateOnly asAt) : base(ctx)
    {
        this.payable = payable;
        this.partyText = partyText;
        this.asAt = asAt;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        var args = cmd.Args.ToList();
        var payable = false;
        if (args.Count > 0 && args[0].ToLowerInvariant() is "payable" or "p" or "receivable" or "r")
        {
            payable = args[0].ToLowerInvariant() is "payable" or "p";
            args.RemoveAt(0);
        }
        var (party, date, _) = ReportArgs.RestAndDate(args, ctx.WorkingDate);
        return new OutstandingViewModel(ctx, payable, party, date ?? ReportArgs.DefaultAsAt(ctx));
    }

    public override string Code => "OUTS";

    public override string Subject => partyName ?? partyText;

    string SideWord => payable ? "payable" : "receivable";

    public override string ExportName => $"OUTS {SideWord} {Iso(asAt)}.csv";

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = null;
        return RunAsync(async () =>
        {
            var r = await Ctx.Host.CallAsync((h, o) => h.Reports.OutstandingAsync(new OutstandingRequest
            {
                Company = Ctx.Company!.Id, AsAt = Iso(asAt), Side = payable ? "Payable" : "Receivable",
            }, o), ct);
            if (partyText.Length == 0) Summary(r);
            else Bills(r);
        });
    }

    void Summary(OutstandingMsg r)
    {
        Heading = Format(payable ? Text.PayablesHeading : Text.ReceivablesHeading, Show(asAt));
        Columns =
        [
            new(Text.PartyColumn, CellAlign.Left), .. BucketHeadings(r.AgeingDays).Select(h => new ReportColumn(h, CellAlign.Right)),
            new(Text.OnAccountColumn, CellAlign.Right), new(Text.TotalColumn, CellAlign.Right),
        ];
        var lines = r.Parties.Select(p =>
        {
            var name = Ctx.Lookups.LedgerName(p.Party);
            return new ReportLine(0,
                [Label(name), .. p.Buckets.Select(b => Amount(new Money(b))), Amount(new Money(p.OnAccount)), Amount(new Money(p.Total))],
                ReportArgs.Command("OUTS", $"{SideWord} {name}", asAt), false);
        }).ToList();
        lines.Add(new ReportLine(0,
            [Label(Text.ReportTotal), .. r.BucketTotals.Select(b => Amount(new Money(b))), Amount(new Money(r.OnAccountTotal)), Amount(new Money(r.Total))],
            null, true));
        Replace(lines);
    }

    void Bills(OutstandingMsg r)
    {
        Columns =
        [
            new(Text.BillColumn, CellAlign.Left), new(Text.PartyDocumentColumn, CellAlign.Left), new(Text.DateColumn, CellAlign.Left),
            new(Text.DueColumn, CellAlign.Left), new(Text.DaysOverdueColumn, CellAlign.Right), new(Text.AmountColumn, CellAlign.Right),
            new(Text.OpenColumn, CellAlign.Right),
        ];
        var parties = r.Parties.ToDictionary(p => Ctx.Lookups.LedgerName(p.Party));
        var match = NameMatcher.Match(partyText, [.. parties.Keys]);
        if (match.Kind is MatchKind.None or MatchKind.Ambiguous)
        {
            Message = Format(match.Kind == MatchKind.None ? Text.NoPartyNamed : Text.PartyNameAmbiguous, partyText);
            Replace([]);
            return;
        }
        partyName = match.Candidates[0];
        OnPropertyChanged(nameof(Subject));
        Heading = Format(payable ? Text.PayablesPartyHeading : Text.ReceivablesPartyHeading, partyName, Show(asAt));
        var party = parties[partyName];
        var lines = party.Bills.Select(b => new ReportLine(0,
        [
            Label(b.Number), Label(b.PartyDocNumber), Label(Show(FromIso(b.Date))), Label(Show(FromIso(b.Due))),
            Label(b.DaysOverdue.ToString(CultureInfo.InvariantCulture)), Amount(new Money(b.Amount)), Amount(new Money(b.Open)),
        ], b.VoucherType.Length > 0 ? $"{VoucherCodes.CodeOf(b.VoucherType)} {b.Number}" : null, false)).ToList();
        lines.Add(new ReportLine(0, [Label(Text.OnAccountText), Blank, Blank, Blank, Blank, Blank, Amount(new Money(party.OnAccount))], null, false));
        lines.Add(new ReportLine(0, [Label(Text.ReportTotal), Blank, Blank, Blank, Blank, Blank, Amount(new Money(party.Total))], null, true));
        Replace(lines);
    }

    /// <summary>"Not due", then "1-30", "31-60", and so on, then "Over 90".</summary>
    static IEnumerable<string> BucketHeadings(IReadOnlyList<int> days)
    {
        yield return Text.NotDueColumn;
        var from = 1;
        foreach (var d in days)
        {
            yield return Format(Text.DaysRangeColumn, from, d);
            from = d + 1;
        }
        yield return Format(Text.OverDaysColumn, days.Count > 0 ? days[^1] : 0);
    }
}
