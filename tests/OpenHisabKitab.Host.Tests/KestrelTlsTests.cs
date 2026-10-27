using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Host.Grpc;
using OpenHisabKitab.Host.Security;

namespace OpenHisabKitab.Host.Tests;

public sealed class KestrelTlsTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task Real_host_serves_get_info_over_tls_with_the_recorded_certificate()
    {
        await using var app = HostApp.Build(["--Host:DataDirectory=" + folder, "--Host:GrpcPort=0"]);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var port = new Uri(address.Replace("[::]", "localhost", StringComparison.Ordinal)).Port;

        var expected = File.ReadAllText(Path.Combine(folder, "host-cert-fingerprint.txt")).Trim();
        string? seen = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
            {
                seen = CertificateStore.Fingerprint(cert!);
                return seen == expected;
            },
        };
        using var channel = GrpcChannel.ForAddress($"https://localhost:{port}", new GrpcChannelOptions { HttpHandler = handler });
        var info = await new Contracts.V1.Session.SessionClient(channel).GetInfoAsync(new Contracts.V1.Empty());

        Assert.Equal(HostVersion.Current, info.Version);
        Assert.True(info.NeedsSetup);
        Assert.Equal(expected, seen);
        Assert.True(File.Exists(Path.Combine(folder, "host.db")));
        await app.StopAsync();
    }
}
