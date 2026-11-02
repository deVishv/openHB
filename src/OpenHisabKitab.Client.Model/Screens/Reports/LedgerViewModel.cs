using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// LEDG name [period]: one ledger's statement with running balance (spec 6.3), 500 lines a page. The opening balance
/// comes first and the closing balance once the last page is in. A line drills to its voucher.
/// </summary>
public sealed class LedgerViewModel : ReportScreen
{
    const int PageSize = 500;
    readonly DateRange period;
    readonly string nameText;
    readonly string? error;
    LedgerMsg? ledger;
    string next = "";

    LedgerViewModel(ShellContext ctx, string nameText, DateRange period, string? error) : base(ctx)
    {
        this.nameText = nameText;
        this.period = period;
        this.error = error;
        Columns =
        [
            new(Text.DateColumn, CellAlign.Left), new(Text.ParticularsColumn, CellAlign.Left), new(Text.TypeColumn, CellAlign.Left),
            new(Text.NumberColumn, CellAlign.Left), new(Text.DebitColumn, CellAlign.Right), new(Text.CreditColumn, CellAlign.Right),
            new(Text.BalanceColumn, CellAlign.Right),
        ];
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        var (name, period, error) = ReportArgs.NameAndPeriod(cmd.Args, ctx.WorkingDate);
        return new LedgerViewModel(ctx, name, period ?? ReportArgs.DefaultPeriod(ctx), error);
    }

    public override string Code => "LEDG";

    public override string Subject => ledger?.Name ?? nameText;

    public override string ExportName => $"LEDG {Iso(period.From)} {Iso(period.To)}.csv";

    public override Task LoadAsync(CancellationToken ct)
    {
        Message = error;
        if (error is not null) return Task.CompletedTask;
        if (!FindLedger()) return Task.CompletedTask;
        Heading = Format(Text.LedgerHeading, ledger!.Name, Show(period.From), Show(period.To));
        return RunAsync(async () =>
        {
            next = "";
            var page = await Page(ct);
            var lines = new List<ReportLine> { Balance(Text.OpeningBalanceLine, new Money(page.Opening)) };
            Add(lines, page);
            Replace(lines);
        });
    }

    public override Task MoreAsync() => HasMore ? RunAsync(async () =>
    {
        var lines = new List<ReportLine>();
        Add(lines, await Page(CancellationToken.None));
        foreach (var l in lines) Lines.Add(l);
    }) : Task.CompletedTask;

    bool FindLedger()
    {
        if (nameText.Length == 0)
        {
            Message = Text.LedgerNameNeeded;
            return false;
        }
        var ledgers = Ctx.Lookups.Chart.Ledgers;
        var match = NameMatcher.Match(nameText, [.. ledgers.Select(l => l.Name)]);
        switch (match.Kind)
        {
            case MatchKind.None:
                Message = Format(Text.NoLedgerNamed, nameText);
                return false;
            case MatchKind.Ambiguous:
                Message = Format(Text.LedgerNameAmbiguous, nameText);
                return false;
        }
        ledger = ledgers.First(l => l.Name == match.Candidates[0]);
        OnPropertyChanged(nameof(Subject));
        return true;
    }

    async Task<StatementPageMsg> Page(CancellationToken ct)
    {
        var page = await Ctx.Host.CallAsync((h, o) => h.Reports.LedgerStatementPageAsync(new StatementPageRequest
        {
            Company = Ctx.Company!.Id, Ledger = ledger!.Id, Period = Wire(period), Page = new PageMsg { Limit = PageSize, After = next },
        }, o), ct);
        next = page.Next;
        HasMore = next.Length > 0;
        return page;
    }

    void Add(List<ReportLine> lines, StatementPageMsg page)
    {
        foreach (var l in page.Lines)
        {
            var amount = new Money(l.Amount);
            var code = VoucherCodes.CodeOf(l.Type);
            lines.Add(new ReportLine(0,
            [
                Label(Show(FromIso(l.Date))), Label(Particulars(l)), Label(code), Label(l.Number),
                amount > Money.Zero ? Amount(amount) : Blank, amount < Money.Zero ? Amount(-amount) : Blank, DrCr(new Money(l.Balance)),
            ], $"{code} {l.Number}", false));
        }
        if (!HasMore) lines.Add(Balance(Text.ClosingBalanceLine, new Money(page.Closing)));
    }

    string Particulars(StatementLineMsg l)
    {
        if (l.CounterLedgers.Count == 0) return "";
        var first = Ctx.Lookups.LedgerName(l.CounterLedgers[0]);
        return l.CounterLedgers.Count == 1 ? first : Format(Text.ParticularsMore, first, l.CounterLedgers.Count - 1);
    }

    static ReportLine Balance(string label, Money balance) =>
        new(0, [Blank, Label(label), Blank, Blank, Blank, Blank, DrCr(balance)], null, true);
}
