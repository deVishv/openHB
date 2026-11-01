using Grpc.Core;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>
/// Follows the host's change stream while connected (spec 4, Transport). A change to the open company marks each affected
/// tab "Changed", or reloads it when reports.autoReload is on. When the stream drops, one ordinary call finds out why, which
/// starts the connection's own retry or asks for login.
/// </summary>
public sealed class ChangeWatcher(ShellContext ctx, TabsViewModel tabs, Action<Action> post, Func<IScreen, Task> reload)
{
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (ctx.Host.State != ConnectionState.Connected)
            {
                try { await ConnectedAsync(ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }
            try
            {
                using var call = ctx.Host.Watch(ct);
                await foreach (var change in call.ResponseStream.ReadAllAsync(ct)) post(() => _ = HandleAsync(change));
            }
            catch (OperationCanceledException) { return; }
            catch (RpcException) when (!ct.IsCancellationRequested) { }
            catch (RpcException) { return; }
            try
            {
                await ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o), ct);
                // Still connected, so the stream ended for another reason; wait a little before opening it again.
                await Task.Delay(TimeSpan.FromSeconds(1), ctx.Time, ct);
            }
            catch (HostException) { }
            catch (OperationCanceledException) { return; }
        }
    }

    async Task HandleAsync(ChangeEventMsg change)
    {
        if (ctx.Company?.Id != change.Company) return;
        if (change.Kind == "Settings") await ctx.Settings.LoadAsync(ctx.Company.Id);
        try { await ctx.Lookups.OnChangeAsync(change); }
        catch (HostException e) { ctx.Report(e.Message); }
        var autoReload = ctx.Settings.Current.Bool("reports.autoReload");
        foreach (var tab in tabs.Tabs.ToList().Where(t => t.Screen.IsAffectedBy(change)))
        {
            if (autoReload) await reload(tab.Screen);
            else tab.Changed = true;
        }
    }

    async Task ConnectedAsync(CancellationToken ct)
    {
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnState(ConnectionState s)
        {
            if (s == ConnectionState.Connected) connected.TrySetResult();
        }
        ctx.Host.StateChanged += OnState;
        try
        {
            if (ctx.Host.State == ConnectionState.Connected) connected.TrySetResult();
            await connected.Task.WaitAsync(ct);
        }
        finally { ctx.Host.StateChanged -= OnState; }
    }
}
