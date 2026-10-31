using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>
/// LOCK: locks the books up to a date (spec 5.6). Ctrl+S locks; moving the lock earlier unlocks a period and asks first.
/// Only roles with the unlock right can unlock; the host checks every rule.
/// </summary>
public sealed partial class LockViewModel : ScreenViewModel
{
    DateOnly? lockedUpTo;

    LockViewModel(ShellContext ctx, string date) : base(ctx) => NewDate = date;

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new LockViewModel(ctx, string.Join(' ', cmd.Args));

    public override string Code => "LOCK";

    public bool CanLock => Ctx.Has("LockPeriod");

    public bool CanUnlock => Ctx.Has("UnlockPeriod") && lockedUpTo is not null;

    /// <summary>"Locked up to 30-06-2026", or "Not locked".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUnlock))]
    public partial string Current { get; private set; } = "";

    [ObservableProperty]
    public partial string NewDate { get; set; }

    public override Task LoadAsync(CancellationToken ct) => RunAsync(async () =>
    {
        var reply = await Ctx.Host.CallAsync((h, o) => h.Admin.GetLockAsync(new CompanyRef { Company = Ctx.Company!.Id }, o), ct);
        lockedUpTo = reply.LockedUpTo.Length == 0 ? null : DateOnly.ParseExact(reply.LockedUpTo, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Current = lockedUpTo is { } d ? string.Format(CultureInfo.InvariantCulture, Text.LockedUpTo, Show(d)) : Text.NotLocked;
    });

    public override bool IsAffectedBy(ChangeEventMsg change) => change.Kind is "Lock" or "Restore";

    /// <summary>Ctrl+S: locks up to the date typed.</summary>
    public override async Task SaveAsync()
    {
        Message = null;
        if (!DateInput.TryParse(NewDate, Ctx.WorkingDate, out var date))
        {
            Message = Text.VoucherDateInvalid;
            return;
        }
        if (lockedUpTo is { } current && date < current
            && !await Ctx.Confirm.AskAsync(string.Format(CultureInfo.InvariantCulture, Text.UnlockAfterQuestion, Show(date)))) return;
        if (await RunAsync(async () => await Ctx.Host.CallAsync((h, o) => h.Admin.LockUpToAsync(new LockRequest
            {
                Company = Ctx.Company!.Id, Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, o))))
            await LoadAsync(CancellationToken.None);
    }

    public async Task UnlockAsync()
    {
        if (!CanUnlock || !await Ctx.Confirm.AskAsync(Text.UnlockAllQuestion)) return;
        if (await RunAsync(async () => await Ctx.Host.CallAsync((h, o) => h.Admin.UnlockAsync(new CompanyRef { Company = Ctx.Company!.Id }, o))))
            await LoadAsync(CancellationToken.None);
    }

    string Show(DateOnly d) => DateText.Format(d, Ctx.Settings.Current.Text("ui.dateFormat"));
}
