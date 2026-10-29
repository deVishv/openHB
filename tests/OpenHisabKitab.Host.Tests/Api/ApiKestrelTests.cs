using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Host.Security;

namespace OpenHisabKitab.Host.Tests.Api;

/// <summary>The real host on real ports: where the API port listens and what each port refuses.</summary>
public sealed class ApiKestrelTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    int SeedSettings(bool allowLan)
    {
        var port = FreePort();
        using var c = new HostData(folder).OpenHost();
        using (var tx = SqliteDatabase.BeginWrite(c))
        {
            HostSettingsRepository.Set(tx, "api.enabled", "true");
            HostSettingsRepository.Set(tx, "api.port", port.ToString(CultureInfo.InvariantCulture));
            HostSettingsRepository.Set(tx, "api.allowLan", allowLan ? "true" : "false");
            tx.Commit();
        }
        return port;
    }

    HttpClientHandler Pinned() => new()
    {
        ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            CertificateStore.Fingerprint(cert!) == File.ReadAllText(Path.Combine(folder, "host-cert-fingerprint.txt")).Trim(),
    };

    static IReadOnlyList<string> Addresses(WebApplication app) =>
        [.. app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses];

    static int PortOf(string address) => new Uri(address.Replace("[::]", "localhost", StringComparison.Ordinal)).Port;

    [Fact]
    public async Task Api_port_binds_loopback_only_by_default()
    {
        var apiPort = SeedSettings(allowLan: false);
        await using var app = HostApp.Build(["--Host:DataDirectory=" + folder, "--Host:GrpcPort=0"]);
        await app.StartAsync();
        var addresses = Addresses(app);
        Assert.Contains($"http://127.0.0.1:{apiPort}", addresses);
        var grpcPort = PortOf(addresses.Single(a => PortOf(a) != apiPort));

        using var plain = new HttpClient();
        var r = await plain.GetAsync($"http://127.0.0.1:{apiPort}/api/v1/info");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Contains("\"not_authenticated\"", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var tls = new HttpClient(Pinned());
        using var onGrpc = new HttpRequestMessage(HttpMethod.Get, $"https://localhost:{grpcPort}/api/v1/info")
        {
            Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        Assert.Equal(HttpStatusCode.NotFound, (await tls.SendAsync(onGrpc)).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task Api_port_uses_tls_when_lan_is_allowed()
    {
        var apiPort = SeedSettings(allowLan: true);
        await using var app = HostApp.Build(["--Host:DataDirectory=" + folder, "--Host:GrpcPort=0"]);
        await app.StartAsync();
        Assert.Contains($"https://[::]:{apiPort}", Addresses(app));

        using var tls = new HttpClient(Pinned());
        Assert.Equal(HttpStatusCode.Unauthorized, (await tls.GetAsync($"https://localhost:{apiPort}/api/v1/info")).StatusCode);

        using var plain = new HttpClient();
        try { Assert.Equal(HttpStatusCode.BadRequest, (await plain.GetAsync($"http://localhost:{apiPort}/api/v1/info")).StatusCode); }
        catch (HttpRequestException) { }
        await app.StopAsync();
    }
}
