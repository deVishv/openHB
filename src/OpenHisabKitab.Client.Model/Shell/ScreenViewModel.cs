using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>The base of every screen: its one line message and a busy flag, and host failures turned into that message.</summary>
public abstract partial class ScreenViewModel(ShellContext ctx) : ObservableObject, IScreen
{
    protected ShellContext Ctx { get; } = ctx;

    /// <summary>One line shown in the screen, such as the host's reason for refusing a save.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool Busy { get; set; }

    public abstract string Code { get; }

    public virtual string Subject => "";

    public virtual bool HasUnsavedEdits => false;

    public virtual Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

    public virtual bool IsAffectedBy(ChangeEventMsg change) => false;

    public virtual bool Cancel() => false;

    public virtual Task SaveAsync() => Task.CompletedTask;

    public virtual void Find() { }

    /// <summary>Runs host work. A failure becomes <see cref="Message"/>; nothing the user typed is cleared.</summary>
    protected async Task<bool> RunAsync(Func<Task> work)
    {
        Busy = true;
        Message = null;
        try
        {
            await work();
            return true;
        }
        catch (HostException e)
        {
            Message = e.Message;
            return false;
        }
        finally { Busy = false; }
    }
}
