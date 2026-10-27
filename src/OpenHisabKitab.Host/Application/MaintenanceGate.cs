namespace OpenHisabKitab.Host.Application;

/// <summary>Closed while a restore runs. Every company read and write checks it first.</summary>
public sealed class MaintenanceGate
{
    int closed;

    public bool IsClosed => Volatile.Read(ref closed) > 0;

    public void Close() => Interlocked.Increment(ref closed);

    public void Open() => Interlocked.Decrement(ref closed);

    public void Check()
    {
        if (IsClosed) throw new ServiceException(ServiceError.Unavailable, "A restore is running. Try again in a minute.");
    }
}
