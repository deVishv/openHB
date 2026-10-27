using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace OpenHisabKitab.Host.Application;

public enum ChangeKind { Masters, Voucher, Lock, Settings, Restore }

public sealed record ChangeEvent(int CompanyId, ChangeKind Kind, long? EntityId);

/// <summary>
/// Tells connected clients that data changed, so an open report can show its "Changed" marker (spec 4, Transport).
/// A slow subscriber loses its oldest events rather than holding up a save.
/// </summary>
public sealed class ChangeFeed
{
    const int Capacity = 256;

    readonly ConcurrentDictionary<Channel<ChangeEvent>, byte> subscribers = new();

    public int SubscriberCount => subscribers.Count;

    public void Publish(ChangeEvent e)
    {
        foreach (var channel in subscribers.Keys) channel.Writer.TryWrite(e);
    }

    public async IAsyncEnumerable<ChangeEvent> Subscribe([EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<ChangeEvent>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        subscribers.TryAdd(channel, 0);
        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(ct)) yield return e;
        }
        finally
        {
            subscribers.TryRemove(channel, out _);
        }
    }
}
