using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>A backup file of the open company. Path is the host's own path, sent back to restore it.</summary>
public sealed record BackupRow(string TakenAt, string File, string Path);

/// <summary>
/// BKUP: the open company's backups, newest first; back up now; restore one (Owner only, after asking); and, for the
/// Owner, the backup schedule held in the host settings (spec 9). The host checks every right.
/// </summary>
public sealed partial class BackupViewModel : ScreenViewModel
{
    const string IntervalKey = "backup.intervalHours";
    const string KeepKey = "backup.keep";
    const string FolderKey = "backup.folder";
    readonly Dictionary<string, string> stored = [];

    BackupViewModel(ShellContext ctx) : base(ctx) { }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new BackupViewModel(ctx);

    public override string Code => "BKUP";

    public bool CanBackup => Ctx.Has("Backup");

    public bool CanRestore => Ctx.Has("Restore");

    public bool CanSchedule => Ctx.Has("ManageHostSettings");

    public ObservableCollection<BackupRow> Rows { get; } = [];

    [ObservableProperty]
    public partial BackupRow? Selected { get; set; }

    [ObservableProperty]
    public partial string IntervalHours { get; set; } = "";

    [ObservableProperty]
    public partial string Keep { get; set; } = "";

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind == "Restore";

    public override Task LoadAsync(CancellationToken ct) => RunAsync(async () =>
    {
        await ListAsync(ct);
        if (!CanSchedule) return;
        var settings = await Ctx.Host.CallAsync((h, o) => h.Admin.ListHostSettingsAsync(new Empty(), o), ct);
        stored.Clear();
        foreach (var s in settings.Settings.Where(s => s.Key is IntervalKey or KeepKey or FolderKey)) stored[s.Key] = Plain(s.Json);
        IntervalHours = stored.GetValueOrDefault(IntervalKey, "");
        Keep = stored.GetValueOrDefault(KeepKey, "");
        Folder = stored.GetValueOrDefault(FolderKey, "");
    });

    public Task BackupNowAsync() => RunAsync(async () =>
    {
        var b = await Ctx.Host.CallAsync((h, o) => h.Admin.BackupNowAsync(new AuditTarget { Company = Ctx.Company!.Id }, o));
        await ListAsync(CancellationToken.None);
        Message = string.Format(CultureInfo.InvariantCulture, Text.BackedUpTo, FileName(b.Path));
    });

    /// <summary>Asks first. The host logs everyone out before it restores, so the session screen follows.</summary>
    public async Task RestoreAsync()
    {
        if (Selected is not { } row)
        {
            Message = Text.ChooseBackupFirst;
            return;
        }
        if (!await Ctx.Confirm.AskAsync(string.Format(CultureInfo.InvariantCulture, Text.RestoreQuestion, row.File))) return;
        await RunAsync(async () => await Ctx.Host.CallAsync((h, o) => h.Admin.RestoreAsync(
            new RestoreRequest { Company = Ctx.Company!.Id, Path = row.Path }, o)));
    }

    /// <summary>Ctrl+S: saves the schedule settings that changed. The host refuses bad values with its own message.</summary>
    public override async Task SaveAsync()
    {
        if (!CanSchedule) return;
        Message = null;
        var changed = new[] { (IntervalKey, IntervalHours), (KeepKey, Keep), (FolderKey, Folder) }
            .Where(x => x.Item2.Trim() != stored.GetValueOrDefault(x.Item1, ""))
            .ToList();
        var ok = await RunAsync(async () =>
        {
            foreach (var (key, text) in changed)
            {
                var json = key == FolderKey ? JsonSerializer.Serialize(text.Trim()) : Number(text);
                await Ctx.Host.CallAsync((h, o) => h.Admin.SetHostSettingAsync(new HostSettingRequest { Key = key, Json = json }, o));
                stored[key] = text.Trim();
            }
        });
        if (ok) Message = Text.BackupScheduleSaved;
    }

    async Task ListAsync(CancellationToken ct)
    {
        var list = await Ctx.Host.CallAsync((h, o) => h.Admin.ListBackupsAsync(new AuditTarget { Company = Ctx.Company!.Id }, o), ct);
        Rows.Clear();
        foreach (var b in list.Backups.OrderByDescending(b => b.TakenAt, StringComparer.Ordinal)) Rows.Add(Row(b));
    }

    BackupRow Row(BackupMsg b)
    {
        var at = TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(b.TakenAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Ctx.Time.LocalTimeZone);
        var when = DateText.Format(DateOnly.FromDateTime(at.DateTime), Ctx.Settings.Current.Text("ui.dateFormat"))
            + at.ToString(" HH:mm", CultureInfo.InvariantCulture);
        return new BackupRow(when, FileName(b.Path), b.Path);
    }

    static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    // A whole number goes as a JSON number; anything else as text, which the host refuses with the setting's own message.
    static string Number(string text) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(CultureInfo.InvariantCulture) : JsonSerializer.Serialize(text.Trim());

    static string Plain(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString()! : doc.RootElement.GetRawText();
    }
}
