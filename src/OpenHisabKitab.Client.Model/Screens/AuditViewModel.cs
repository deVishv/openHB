using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>One audit record as listed. Before and After are the raw JSON the host stored.</summary>
public sealed record AuditRow(long Seq, string At, string User, string Machine, string Source, string Token, string Action, string Entity,
    string Key, string Before, string After);

/// <summary>
/// AUDT [host]: the open company's audit trail, or host.db's, newest first, filtered by dates, user, and entity, with a
/// check of the chain and its anchor (spec 5.5). The host decides who may read which trail.
/// </summary>
public sealed partial class AuditViewModel : ScreenViewModel
{
    const int Limit = 500;
    readonly bool hostTrail;

    AuditViewModel(ShellContext ctx, bool hostTrail) : base(ctx) => this.hostTrail = hostTrail;

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) =>
        new AuditViewModel(ctx, cmd.Args.Count > 0 && cmd.Args[0].Equals("host", StringComparison.OrdinalIgnoreCase));

    public override string Code => "AUDT";

    public override string Subject => hostTrail ? "host" : "";

    public string Heading => hostTrail ? Text.AuditHostHeading : Text.AuditHeading;

    [ObservableProperty]
    public partial string From { get; set; } = "";

    [ObservableProperty]
    public partial string To { get; set; } = "";

    [ObservableProperty]
    public partial string User { get; set; } = "";

    [ObservableProperty]
    public partial string Entity { get; set; } = "";

    public ObservableCollection<AuditRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Before), nameof(After))]
    public partial AuditRow? Selected { get; set; }

    public string Before => Pretty(Selected?.Before);

    public string After => Pretty(Selected?.After);

    [ObservableProperty]
    public partial string Verification { get; private set; } = "";

    int Target => hostTrail ? 0 : Ctx.Company!.Id;

    public override Task LoadAsync(CancellationToken ct) => SearchAsync();

    public override bool IsAffectedBy(ChangeEventMsg change) => !hostTrail;

    public async Task SearchAsync()
    {
        Message = null;
        if (!Day(From, out var from) || !Day(To, out var to))
        {
            Message = Text.VoucherDateInvalid;
            return;
        }
        var request = new ListAuditRequest
        {
            Company = Target, User = User.Trim(), Entity = Entity.Trim(), Limit = Limit,
            From = from is { } f ? At(f.ToDateTime(TimeOnly.MinValue)) : "",
            To = to is { } t ? At(t.ToDateTime(TimeOnly.MaxValue)) : "",
        };
        // A reload after a change keeps the record the user was reading.
        var kept = Selected?.Seq;
        Rows.Clear();
        Selected = null;
        await RunAsync(async () =>
        {
            var list = await Ctx.Host.CallAsync((h, o) => h.Admin.ListAuditAsync(request, o));
            foreach (var e in list.Entries) Rows.Add(Row(e));
            Selected = Rows.FirstOrDefault(r => r.Seq == kept);
            if (Rows.Count == Limit) Message = string.Format(CultureInfo.InvariantCulture, Text.AuditPageFull, Limit);
        });
    }

    public Task VerifyAsync() => RunAsync(async () =>
    {
        var v = await Ctx.Host.CallAsync((h, o) => h.Admin.VerifyAuditAsync(new AuditTarget { Company = Target }, o));
        Verification = v.FirstBadSeq > 0 ? string.Format(CultureInfo.InvariantCulture, Text.ChainBroken, v.FirstBadSeq)
            : v.Anchor == "Mismatch" ? Text.ChainAnchorMismatch
            : v.Anchor == "Missing" ? Text.ChainAnchorMissing
            : string.Format(CultureInfo.InvariantCulture, Text.ChainIntact, v.Count);
    });

    bool Day(string text, out DateOnly? day)
    {
        day = null;
        if (text.Trim().Length == 0) return true;
        if (!DateInput.TryParse(text, Ctx.WorkingDate, out var d)) return false;
        day = d;
        return true;
    }

    // A local day boundary as a round trip UTC timestamp, the form the host stores.
    string At(DateTime local) =>
        new DateTimeOffset(local, Ctx.Time.LocalTimeZone.GetUtcOffset(local)).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    AuditRow Row(AuditEntryMsg e)
    {
        var at = DateTimeOffset.Parse(e.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var local = TimeZoneInfo.ConvertTime(at, Ctx.Time.LocalTimeZone);
        var when = DateText.Format(DateOnly.FromDateTime(local.DateTime), Ctx.Settings.Current.Text("ui.dateFormat"))
            + local.ToString(" HH:mm:ss", CultureInfo.InvariantCulture);
        return new AuditRow(e.Seq, when, e.User, e.Machine, e.Source, e.TokenName, e.Action, e.Entity, e.EntityKey, e.Before, e.After);
    }

    static string Pretty(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, Indented);
        }
        catch (JsonException) { return json; }
    }

    static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
