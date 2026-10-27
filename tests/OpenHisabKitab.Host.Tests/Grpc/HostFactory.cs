using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Tests.Grpc;

/// <summary>The real host over an in-memory server, with its data in a temporary folder.</summary>
public sealed class HostFactory(bool callerIsLoopback = true) : WebApplicationFactory<Program>
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Host:DataDirectory", Folder);
        builder.ConfigureServices(s => s.AddSingleton<IRemoteAddressAccessor>(new FixedAddress(callerIsLoopback ? IPAddress.Loopback : null)));
    }

    public GrpcChannel Channel() =>
        GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions { HttpHandler = Server.CreateHandler() });

    /// <summary>Headers every call sends: the host's version, the PC name, and the session token when there is one.</summary>
    public static Metadata Headers(string? token = null, string? version = null)
    {
        var m = new Metadata { { "ohk-version", version ?? HostVersion.Current }, { "ohk-machine", "TESTPC" } };
        if (token is not null) m.Add("authorization", "Bearer " + token);
        return m;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }

    sealed class FixedAddress(IPAddress? address) : IRemoteAddressAccessor
    {
        public IPAddress? RemoteAddress(HttpContext context) => address;
    }
}
