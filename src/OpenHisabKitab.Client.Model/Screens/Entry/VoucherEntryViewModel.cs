using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Screens.Entry;

/// <summary>
/// What every voucher screen shares (spec 6.4): the date and narration, saving with a request id so a retry never makes a
/// second voucher, editing by number with its row version, cancelling, Esc asking once before discarding, and the sub
/// screens for a new party and for bills. A failed save keeps everything typed.
/// </summary>
public abstract partial class VoucherEntryViewModel : ScreenViewModel, IScreenCommands, IEntryLines
{
    readonly string code;
    bool loading;

    protected VoucherEntryViewModel(ShellContext ctx, string code) : base(ctx)
    {
        this.code = code;
        Date = ShowDate(ctx.WorkingDate);
    }

    public override string Code => code;

    /// <summary>The host's type name, such as "Receipt".</summary>
    protected string VoucherType => VoucherCodes.TypeOf(code);

    public override string Subject => Existing?.DisplayNumber ?? "";

    [ObservableProperty]
    public partial string Date { get; set; } = "";

    [ObservableProperty]
    public partial string Narration { get; set; } = "";

    /// <summary>The stored voucher while editing; null for a new one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsCancelled), nameof(Subject))]
    public partial VoucherMsg? Existing { get; private set; }

    public bool IsEditing => Existing is not null;

    public bool IsCancelled => Existing?.Cancelled == true;

    /// <summary>The client's id for this draft's save. Kept across failed tries; renewed after a save.</summary>
    public string RequestId { get; private set; } = NewId();

    /// <summary>A PartiesViewModel while a new party is being created from this voucher.</summary>
    [ObservableProperty]
    public partial object? SubScreen { get; protected set; }

    /// <summary>Open while allocating a line's party to its bills.</summary>
    [ObservableProperty]
    public partial BillsPanelViewModel? Bills { get; protected set; }

    [ObservableProperty]
    public partial string Totals { get; protected set; } = "";

    /// <summary>"Difference 600.00 Dr" until debits equal credits, else "".</summary>
    [ObservableProperty]
    public partial string Difference { get; protected set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial bool Dirty { get; private set; }

    public override bool HasUnsavedEdits => Dirty && !IsCancelled;

    public object? CurrentLine { get; set; }

    public abstract object AddLine();

    protected abstract void RemoveLine(object line);

    /// <summary>Null when the form is not ready; the screen has set Message.</summary>
    protected abstract Task<SavedReply?> SaveNewAsync(DateOnly date);

    protected abstract Task<SavedReply?> SaveEditAsync(DateOnly date, VoucherMsg existing);

    /// <summary>Shows a stored voucher. Called with changes not counted as edits.</summary>
    protected abstract Task FillAsync(VoucherMsg v);

    /// <summary>Empties the form for the next voucher, keeping the date.</summary>
    protected abstract void Clear();

    protected virtual bool CanAllocate => false;

    protected virtual Task OpenBillsAsync() => Task.CompletedTask;

    /// <summary>Esc on the bills panel keeps what was allocated; the screen applies it to its line.</summary>
    protected virtual void CloseBills() => Bills = null;

    /// <summary>A screen calls this once its constructor has filled the form, so a fresh voucher has nothing to discard.</summary>
    protected void Ready() => Dirty = false;

    /// <summary>Screens call this when the user changes anything.</summary>
    protected void Edited()
    {
        if (!loading) Dirty = true;
    }

    partial void OnDateChanged(string value) => Edited();

    partial void OnNarrationChanged(string value) => Edited();

    public override bool IsAffectedBy(ChangeEventMsg change) => IsEditing && change.Kind is "Voucher" or "Lock" && (change.EntityId == 0 || change.EntityId == Existing!.Id);

    public override async Task LoadAsync(CancellationToken ct)
    {
        // Reloading a stored voucher shows its current version; a new draft keeps what was typed.
        if (Existing is { } v && !Dirty) await RunAsync(() => LoadNumberAsync(v.DisplayNumber));
    }

    public override bool Cancel()
    {
        if (SubScreen is PartiesViewModel party) return party.Cancel();
        if (Bills is not null)
        {
            CloseBills();
            return true;
        }
        if (!HasUnsavedEdits) return false;
        _ = DiscardAsync();
        return true;
    }

    async Task DiscardAsync()
    {
        if (!await Ctx.Confirm.AskAsync(Text.DiscardVoucher)) return;
        if (Existing is { } v) await RunAsync(() => LoadNumberAsync(v.DisplayNumber));
        else StartAgain();
    }

    /// <summary>Ctrl+S: saves or edits; the status names the voucher number the host gave.</summary>
    public override async Task SaveAsync()
    {
        if (IsCancelled || Busy) return;
        Message = null;
        if (!DateInput.TryParse(Date, Ctx.WorkingDate, out var date))
        {
            Message = Text.VoucherDateInvalid;
            return;
        }
        Busy = true;
        try
        {
            var existing = Existing;
            var reply = existing is null ? await SaveNewAsync(date) : await SaveEditAsync(date, existing);
            if (reply is null) return;
            if (reply.AlreadySaved)
            {
                // The first try saved; the screen shows what was stored, not the draft as it is now.
                await LoadNumberAsync(reply.DisplayNumber);
                RequestId = NewId();
                Message = Format(Text.AlreadySavedShowing, reply.DisplayNumber);
                return;
            }
            Ctx.Report(Format(Text.SavedVoucher, reply.DisplayNumber));
            if (existing is not null) await LoadNumberAsync(reply.DisplayNumber);
            else StartAgain();
        }
        catch (HostException e) when (e.Kind == HostErrorKind.Conflict)
        {
            var current = e.Current(VoucherMsg.Parser);
            await Show(current);
            Message = e.Message;
        }
        catch (HostException e) { Message = e.Message; }
        finally { Busy = false; }
    }

    /// <summary>Opens a stored voucher by its number to edit it.</summary>
    protected async Task LoadNumberAsync(string number)
    {
        var v = await Ctx.Host.CallAsync((h, o) => h.Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = Ctx.Company!.Id, Type = VoucherType, Number = number }, o));
        await Show(v);
    }

    async Task Show(VoucherMsg v)
    {
        loading = true;
        try
        {
            Existing = v;
            Bills = null;
            SubScreen = null;
            Date = ShowDate(DateOnly.ParseExact(v.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
            Narration = v.Narration;
            await FillAsync(v);
        }
        finally
        {
            loading = false;
            Dirty = false;
        }
    }

    void StartAgain()
    {
        loading = true;
        try
        {
            Existing = null;
            Bills = null;
            SubScreen = null;
            Narration = "";
            Clear();
        }
        finally
        {
            loading = false;
            Dirty = false;
            RequestId = NewId();
        }
    }

    public Task CancelVoucherAsync() => Existing is { Cancelled: false } v
        ? RunAsync(async () =>
        {
            if (!await Ctx.Confirm.AskAsync(Format(Text.CancelVoucherQuestion, v.DisplayNumber))) return;
            await Ctx.Host.CallAsync((h, o) => h.Vouchers.CancelAsync(new CancelRequest { Company = Ctx.Company!.Id, Id = v.Id, RowVersion = v.RowVersion }, o));
            Ctx.Report(Format(Text.CancelledVoucher, v.DisplayNumber));
            await LoadNumberAsync(v.DisplayNumber);
        })
        : Task.CompletedTask;

    public virtual bool CanRun(string command) => command switch
    {
        "voucher.cancelVoucher" => Existing is { Cancelled: false } && Ctx.Has("CancelVoucher"),
        "voucher.bills" => !IsCancelled && CanAllocate,
        "line.add" => !IsCancelled,
        "line.remove" => !IsCancelled && CurrentLine is not null,
        _ => false,
    };

    public async Task RunAsync(string command)
    {
        switch (command)
        {
            case "voucher.cancelVoucher": await CancelVoucherAsync(); break;
            case "voucher.bills": await RunAsync(OpenBillsAsync); break;
            case "line.add": AddLine(); Edited(); break;
            case "line.remove" when CurrentLine is { } line:
                RemoveLine(line);
                CurrentLine = null;
                Edited();
                break;
        }
    }

    protected string ShowDate(DateOnly d) => DateText.Format(d, Ctx.Settings.Current.Text("ui.dateFormat"));

    protected static string Wire(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    static string NewId() => Guid.NewGuid().ToString("N");
}
