using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// The base of every report screen: a heading, columns, and lines that one view shows; Enter on a line runs its drill-down
/// command (spec 6.3); Ctrl+E exports CSV for users with the export right. Figures are the host's, only formatted here.
/// </summary>
public abstract partial class ReportScreen(ShellContext ctx) : ScreenViewModel(ctx), IScreenCommands
{
    [ObservableProperty]
    public partial IReadOnlyList<ReportColumn> Columns { get; protected set; } = [];

    public ObservableCollection<ReportLine> Lines { get; } = [];

    [ObservableProperty]
    public partial string Heading { get; protected set; } = "";

    [ObservableProperty]
    public partial bool HasMore { get; protected set; }

    public bool CanExport => Ctx.Has("Export");

    /// <summary>A file name for the export, such as "TRLB 2026-04-01 2027-03-31.csv".</summary>
    public abstract string ExportName { get; }

    /// <summary>Raised by Ctrl+E; the view asks where to save, then calls <see cref="WriteCsv"/>.</summary>
    public event Action? ExportRequested;

    /// <summary>Raised after the lines are replaced, so the view can put the cursor on the first line.</summary>
    public event Action? Loaded;

    public virtual Task MoreAsync() => Task.CompletedTask;

    public void WriteCsv(Stream to) => CsvExport.Write(Columns, Lines, to);

    /// <summary>Runs the line's drill-down command in this tab, or a new one, as typing it would.</summary>
    public Task OpenAsync(ReportLine line, bool newTab) =>
        line.Drill is { } command && Ctx.Run is { } run ? run(command, newTab) : Task.CompletedTask;

    public virtual bool CanRun(string command) => command == "report.export" && CanExport;

    /// <summary>Ctrl+E loads every remaining page first, so an export is never a partial report that looks whole.</summary>
    public virtual async Task RunAsync(string command)
    {
        if (command != "report.export" || !CanExport) return;
        while (HasMore)
        {
            var before = Lines.Count;
            await MoreAsync();
            // A page that failed shows its message; export nothing rather than part of the report.
            if (Lines.Count == before) return;
        }
        ExportRequested?.Invoke();
    }

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind is "Voucher" or "Masters" or "Settings" or "Lock" or "Restore";

    protected void Replace(IEnumerable<ReportLine> lines)
    {
        Lines.Clear();
        foreach (var l in lines) Lines.Add(l);
        Loaded?.Invoke();
    }

    protected string Show(DateOnly d) => DateText.Format(d, Ctx.Settings.Current.Text("ui.dateFormat"));

    protected ReportCell Amount(Money m) => new(AmountText.Format(m, Ctx.Settings.Current), m);

    protected static ReportCell DrCr(Money m) => new(AmountText.DrCr(m), m);

    protected static ReportCell Label(string text) => new(text);

    protected static ReportCell Blank => new("");

    protected static Period Wire(DateRange r) => new() { From = Iso(r.From), To = Iso(r.To) };

    protected static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected static DateOnly FromIso(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected static string Format(string pattern, params object[] args) => string.Format(CultureInfo.InvariantCulture, pattern, args);
}
