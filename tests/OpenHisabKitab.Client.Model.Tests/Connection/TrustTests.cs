using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Host;

namespace OpenHisabKitab.Client.Model.Tests.Connection;

/// <summary>Trust on first use against the real host over TLS (spec 4, Transport).</summary>
public sealed class TrustTests : IAsyncLifetime
{
    readonly string hostFolder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
    readonly string clientFolder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
    Microsoft.AspNetCore.Builder.WebApplication app = null!;
    HostAddress address = null!;
    KnownHosts known = null!;
    HostConnection connection = null!;

    string Expected => File.ReadAllText(Path.Combine(hostFolder, "host-cert-fingerprint.txt")).Trim();

    public async Task InitializeAsync()
    {
        app = HostApp.Build(["--Host:DataDirectory=" + hostFolder, "--Host:GrpcPort=0"]);
        await app.StartAsync();
        var listening = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        address = new HostAddress("localhost", new Uri(listening.Replace("[::]", "localhost", StringComparison.Ordinal)).Port);
        Directory.CreateDirectory(clientFolder);
        known = new KnownHosts(Path.Combine(clientFolder, "known-hosts.json"));
        connection = new HostConnection(new TlsChannelFactory(), known, TimeProvider.System, ProductVersion.Current, "TESTPC");
    }

    public async Task DisposeAsync()
    {
        await connection.DisposeAsync();
        await app.StopAsync();
        await app.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { hostFolder, clientFolder })
            try { Directory.Delete(f, recursive: true); }
            catch (IOException) { }
    }

    [Fact]
    public async Task Unknown_certificate_needs_trust_and_sends_no_password()
    {
        Assert.Equal(ConnectionState.NeedsTrust, await connection.ConnectAsync(address));
        Assert.Equal(Expected, connection.SeenFingerprint);
        Assert.Null(connection.PinnedFingerprint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync("owner", "x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SetupOwnerAsync("owner", "owner-password-1"));
        Assert.Equal(ConnectionState.NeedsSetup, await connection.TrustSeenCertificateAsync());
        Assert.Equal(Expected, known.Fingerprint(address));
    }

    [Fact]
    public async Task Trusted_certificate_connects_straight_away()
    {
        known.Trust(address, Expected);
        Assert.Equal(ConnectionState.NeedsSetup, await connection.ConnectAsync(address));
        await connection.SetupOwnerAsync("owner", "owner-password-1");
        Assert.Equal(ConnectionState.Connected, connection.State);
    }

    [Fact]
    public async Task Changed_certificate_blocks_and_shows_both_fingerprints()
    {
        known.Trust(address, "00:11");
        Assert.Equal(ConnectionState.CertificateChanged, await connection.ConnectAsync(address));
        Assert.Equal(("00:11", Expected), (connection.PinnedFingerprint, connection.SeenFingerprint));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync("owner", "x"));
        Assert.Equal(ConnectionState.NeedsSetup, await connection.TrustSeenCertificateAsync());
        Assert.Equal(Expected, known.Fingerprint(address));
    }

    [Fact]
    public async Task Nothing_listening_says_check_the_address()
    {
        await app.StopAsync();
        Assert.Equal(ConnectionState.Disconnected, await connection.ConnectAsync(address));
        Assert.Equal("Host not reachable. Check the host name and port.", connection.Message);
    }
}
