using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>
/// DATE: the working date for new vouchers and reports (spec 6.3). "DATE 15-4" sets it at once with no tab; with no date,
/// or one that cannot be read, the screen opens and asks (spec 6.2).
/// </summary>
public sealed partial class DateViewModel : ScreenViewModel
{
    DateViewModel(ShellContext ctx) : base(ctx) => Input = Show(ctx.WorkingDate);

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx)
    {
        if (ctx.Replaying) return new DateViewModel(ctx) { Input = string.Join(' ', cmd.Args) };
        if (cmd.Args.Count == 1 && Apply(ctx, cmd.Args[0]) is { Problem: null, Done: var done })
        {
            ctx.Report(done);
            return null;
        }
        var screen = new DateViewModel(ctx);
        if (cmd.Args.Count > 0) screen.Message = Apply(ctx, string.Join(' ', cmd.Args)).Problem;
        return screen;
    }

    public override string Code => "DATE";

    [ObservableProperty]
    public partial string Input { get; set; }

    /// <summary>Ctrl+S or Enter applies the typed date.</summary>
    public override Task SaveAsync()
    {
        var (problem, done) = Apply(Ctx, Input.Trim());
        Message = problem ?? done;
        if (problem is null)
        {
            Ctx.Report(done);
            Input = Show(Ctx.WorkingDate);
        }
        return Task.CompletedTask;
    }

    /// <summary>Sets the date when it can be read and a year of the open company holds it; the year follows the date.</summary>
    static (string? Problem, string Done) Apply(ShellContext ctx, string text)
    {
        // "today" and "15-4" are read against the calendar, not the working date being changed.
        if (!DateInput.TryParse(text, DateOnly.FromDateTime(ctx.Time.GetLocalNow().DateTime), out var date)) return (Text.EnterDate, "");
        string Show(DateOnly d) => DateText.Format(d, ctx.Settings.Current.Text("ui.dateFormat"));
        if (ctx.Company is not null)
        {
            var holding = ctx.Years.Where(y => y.Range.Contains(date)).Select(y => (FinancialYear?)y).FirstOrDefault();
            if (holding is not { } year) return (string.Format(CultureInfo.InvariantCulture, Text.NoYearHolds, Show(date)), "");
            if (ctx.Year != year) ctx.Year = year;
        }
        ctx.WorkingDate = date;
        return (null, string.Format(CultureInfo.InvariantCulture, Text.WorkingDateSet, Show(date)));
    }

    string Show(DateOnly d) => DateText.Format(d, Ctx.Settings.Current.Text("ui.dateFormat"));
}
