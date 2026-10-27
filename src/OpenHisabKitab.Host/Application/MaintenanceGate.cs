using System.Collections.Concurrent;

namespace OpenHisabKitab.Host.Application;

/// <summary>Closed for one company while it is restored. Every read and write of that company checks it first.</summary>
public sealed class MaintenanceGate
{
    readonly ConcurrentDictionary<int, int> closed = new();

    public bool IsClosed(int company) => closed.GetValueOrDefault(company) > 0;

    public void Close(int company) => closed.AddOrUpdate(company, 1, (_, n) => n + 1);

    public void Open(int company) => closed.AddOrUpdate(company, 0, (_, n) => Math.Max(0, n - 1));

    public void Check(int company)
    {
        if (IsClosed(company)) throw new ServiceException(ServiceError.Unavailable, "A restore is running. Try again in a minute.");
    }
}
