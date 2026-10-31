using Grpc.Net.Client;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>An open channel, and the fingerprint of the certificate the host showed, once a call has reached it.</summary>
public sealed class ChannelOpening(GrpcChannel channel) : IDisposable
{
    public GrpcChannel Channel { get; } = channel;

    public string? SeenFingerprint { get; set; }

    public void Dispose() => Channel.Dispose();
}

/// <summary>Opens channels to a host. The channel accepts only a certificate with the pinned fingerprint.</summary>
public interface IChannelFactory
{
    ChannelOpening Open(HostAddress host, string? pinnedFingerprint);
}
