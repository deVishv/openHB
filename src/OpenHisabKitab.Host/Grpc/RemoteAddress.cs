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

/// <summary>True for a loopback caller, including an IPv4 caller seen through a dual mode IPv6 socket.</summary>
public static class Loopback
{
    public static bool Is(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
