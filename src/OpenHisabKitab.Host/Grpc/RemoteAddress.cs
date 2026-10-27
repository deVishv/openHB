using System.Net;

namespace OpenHisabKitab.Host.Grpc;

/// <summary>Where a call came from. Tests replace it, because the in-memory test server has no remote address.</summary>
public interface IRemoteAddressAccessor
{
    IPAddress? RemoteAddress(HttpContext context);
}

internal sealed class HttpRemoteAddress : IRemoteAddressAccessor
{
    public IPAddress? RemoteAddress(HttpContext context) => context.Connection.RemoteIpAddress;
}
