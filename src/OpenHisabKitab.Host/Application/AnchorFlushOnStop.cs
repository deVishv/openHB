using Microsoft.Extensions.Hosting;

namespace OpenHisabKitab.Host.Application;

/// <summary>Writes the audit anchors still waiting when the host stops, so none are missing through the downtime.</summary>
public sealed class AnchorFlushOnStop(CompanyHub hub) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => hub.FlushAnchorsAsync();
}
