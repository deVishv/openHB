using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Host.Api;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.McpBridge.Tests;

/// <summary>The real host in memory, with the API and MCP on and every request treated as arriving on the API port from the host PC.</summary>
public sealed class BridgeHost : WebApplicationFactory<Program>
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Host:DataDirectory", folder);
        builder.ConfigureServices(s =>
        {
            s.AddSingleton<IRemoteAddressAccessor>(new Loopback());
            s.AddSingleton<IListenerAccessor>(new AlwaysApiPort());
        });
    }

    /// <summary>Turns on the API and MCP and returns a read only token of the Owner.</summary>
    public string OwnerToken()
    {
        var owner = Services.GetRequiredService<AuthService>().SetupOwner("owner", "owner-pass-1", "HOSTPC", isLoopback: true).Context;
        var settings = Services.GetRequiredService<HostSettingsService>();
        settings.Set(owner, "api.enabled", "true");
        settings.Set(owner, "mcp.enabled", "true");
        return Services.GetRequiredService<ApiTokenService>().Create(owner, "bridge", readWrite: false).Token;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    sealed class Loopback : IRemoteAddressAccessor
    {
        public IPAddress? RemoteAddress(HttpContext context) => IPAddress.Loopback;
    }

    sealed class AlwaysApiPort : IListenerAccessor
    {
        public bool OnApiPort(HttpContext context) => true;
    }
}
