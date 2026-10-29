using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Api;

/// <summary>Whether a request arrived on the API port. Tests replace it, because the in-memory test server has one port.</summary>
public interface IListenerAccessor
{
    bool OnApiPort(HttpContext context);
}

/// <summary>Uses the port the connection arrived on, never the Host header, which the caller controls.</summary>
internal sealed class KestrelListener(int apiPort) : IListenerAccessor
{
    public bool OnApiPort(HttpContext context) => context.Connection.LocalPort == apiPort;
}

/// <summary>The API port of spec 4.1: loopback only, or every address with TLS when the LAN is allowed.</summary>
public static class ApiListener
{
    public static void Configure(KestrelServerOptions kestrel, ApiSettings settings, X509Certificate2 certificate)
    {
        if (!settings.Enabled) return;
        // HTTP/1.1 only, so gRPC can never be served on this port.
        if (settings.AllowLan)
            kestrel.ListenAnyIP(settings.Port, listen =>
            {
                listen.Protocols = HttpProtocols.Http1;
                listen.UseHttps(certificate);
            });
        else
            kestrel.Listen(IPAddress.Loopback, settings.Port, listen => listen.Protocols = HttpProtocols.Http1);
    }
}
