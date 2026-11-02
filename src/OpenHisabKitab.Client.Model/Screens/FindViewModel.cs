using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>A voucher FIND found. Amount is its debit total.</summary>
public sealed record FoundRow(long Id, string Code, string Date, string Number, string Party, string Amount, string Narration, bool Cancelled)
{
    public string Status => Cancelled ? Text.CancelledMarker : "";
}

/// <summary>
/// FIND: vouchers by number, party, amount, or narration (spec 6.3), newest first, 100 to a page. Enter opens one in its own
/// entry screen in this tab, so Back returns here; Shift+Enter opens it in a new tab.
/// </summary>
public sealed partial class FindViewModel : ScreenViewModel
{
    const int PageSize = 100;
    string next = "";
    bool fresh;

    FindViewModel(ShellContext ctx, string query) : base(ctx) => Query = query;

    public static async Task<IScreen?> CreateAsync(ParsedCommand cmd, ShellContext ctx)
    {
        var screen = new FindViewModel(ctx, string.Join(' ', cmd.Args));
        if (screen.Query.Length > 0)
        {
            await screen.SearchAsync();
            screen.fresh = true;
        }
        return screen;
    }

    public override string Code => "FIND";

    public override string Subject => Query.Trim();

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    public ObservableCollection<FoundRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    /// <summary>The view focuses the query box when this is raised (FIND alone, or Ctrl+F).</summary>
    public event Action? FindRequested;

    public override void Find() => FindRequested?.Invoke();

    public override Task LoadAsync(CancellationToken ct)
    {
        // A FIND typed with text has just searched; a reload (RFSH, "Changed") searches again.
        if (fresh)
        {
            fresh = false;
            return Task.CompletedTask;
        }
        return Query.Trim().Length > 0 ? SearchAsync() : Task.CompletedTask;
    }

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind is "Voucher" or "Restore";

    public Task SearchAsync() => RunAsync(async () =>
    {
        Rows.Clear();
        next = "";
        await PageAsync();
        OnPropertyChanged(nameof(Subject));
    });

    public Task MoreAsync() => HasMore ? RunAsync(PageAsync) : Task.CompletedTask;

    async Task PageAsync()
    {
        var text = Query.Trim();
        var page = await Ctx.Host.CallAsync((h, o) => h.Vouchers.FindAsync(new FindVouchersRequest
        {
            Company = Ctx.Company!.Id, Text = text, Page = new PageMsg { Limit = PageSize, After = next },
        }, o));
        foreach (var v in page.Vouchers) Rows.Add(Row(v));
        next = page.Next;
        HasMore = next.Length > 0;
        if (Rows.Count == 0) Message = string.Format(CultureInfo.InvariantCulture, Text.NoVouchersMatch, text);
    }

    /// <summary>Opens the voucher as "{CODE} {number}", so Back, Forward, and the tab title work as for any command.</summary>
    public Task OpenAsync(FoundRow row, bool newTab) => Ctx.Run?.Invoke($"{row.Code} {row.Number}", newTab) ?? Task.CompletedTask;

    FoundRow Row(VoucherMsg v)
    {
        var party = Reports.VoucherRowText.Party(v, Ctx.Lookups);
        var amount = Reports.VoucherRowText.DebitTotal(v);
        var date = DateText.Format(DateOnly.ParseExact(v.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture), Ctx.Settings.Current.Text("ui.dateFormat"));
        return new FoundRow(v.Id, VoucherCodes.CodeOf(v.Type), date, v.DisplayNumber, party, v.Cancelled ? "" : IndianNumberFormat.Format(amount),
            v.Narration, v.Cancelled);
    }
}
