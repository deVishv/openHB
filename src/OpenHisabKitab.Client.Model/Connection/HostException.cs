using Google.Protobuf;
using Grpc.Core;
using OpenHisabKitab.Client.Model.Resources;

namespace OpenHisabKitab.Client.Model.Connection;

public enum ConnectionState { Disconnected, Connecting, NeedsTrust, CertificateChanged, VersionMismatch, NeedsSetup, NeedsLogin, Connected, Reconnecting }

public enum HostErrorKind { Unreachable, NeedsLogin, VersionMismatch, Conflict, Refused, TimedOut }

/// <summary>A failed host call, with the one line to show. Conflict carries the record as it now is.</summary>
public sealed class HostException(HostErrorKind kind, string message, Metadata trailers) : Exception(message)
{
    public HostErrorKind Kind { get; } = kind;

    public Metadata Trailers { get; } = trailers;

    /// <summary>The current record the host sent with a stale row version, in the "ohk-current-bin" trailer.</summary>
    public T Current<T>(MessageParser<T> parser) where T : IMessage<T> =>
        parser.ParseFrom(Trailers.Get("ohk-current-bin")?.ValueBytes ?? throw new InvalidOperationException("The host sent no current record."));

    internal static HostException From(RpcException e) => e.StatusCode switch
    {
        StatusCode.Unavailable => new(HostErrorKind.Unreachable, Text.HostUnreachable, e.Trailers),
        // A slow answer is not a lost link: keepalive pings find a dead one. So no reconnect starts.
        StatusCode.DeadlineExceeded => new(HostErrorKind.TimedOut, Text.HostTooSlow, e.Trailers),
        StatusCode.Unauthenticated => new(HostErrorKind.NeedsLogin, e.Status.Detail, e.Trailers),
        StatusCode.FailedPrecondition when e.Status.Detail.StartsWith("Install Open Hisab Kitab", StringComparison.Ordinal) =>
            new(HostErrorKind.VersionMismatch, e.Status.Detail, e.Trailers),
        StatusCode.Aborted => new(HostErrorKind.Conflict, e.Status.Detail, e.Trailers),
        _ => new(HostErrorKind.Refused, e.Status.Detail, e.Trailers),
    };
}
