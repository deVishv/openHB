using Grpc.Net.Client;
using OpenHisabKitab.Contracts;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>
/// gRPC over TLS to the host (spec 4, Transport). The host's certificate is self signed, so it is trusted by its pinned
/// fingerprint alone; with no pin, or another certificate, the handshake fails and the fingerprint seen is kept for the user.
/// </summary>
public sealed class TlsChannelFactory : IChannelFactory
{
    public ChannelOpening Open(HostAddress host, string? pinnedFingerprint)
    {
        ChannelOpening? opening = null;
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            // Keeps the change stream alive through idle routers and notices a dead link (spec 13, Remote ready client).
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null) return false;
            var seen = CertificateFingerprint.Of(cert);
            opening!.SeenFingerprint = seen;
            return seen == pinnedFingerprint;
        };
        var channel = GrpcChannel.ForAddress(new Uri($"https://{host}"), new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true });
        opening = new ChannelOpening(channel);
        return opening;
    }
}
