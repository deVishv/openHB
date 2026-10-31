using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Tests.Grpc;

namespace OpenHisabKitab.Client.Model.Tests;

/// <summary>The real host in memory, and client connections to it with a fake clock for retries.</summary>
public sealed class ClientTestHost : IDisposable
{
    public const string Password = "owner-password-1";
    public const string Machine = "CLIENTPC";
    public const string FakeFingerprint = "AA:BB:CC";

    readonly HostFactory factory = new();
    readonly List<HostConnection> connections = [];

    public ClientTestHost()
    {
        Directory.CreateDirectory(ClientFolder);
        Known = new KnownHosts(Path.Combine(ClientFolder, "known-hosts.json"));
        Known.Trust(Address, FakeFingerprint);
        Channels = new InMemoryChannels(this);
    }

    public string ClientFolder { get; } = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 6, 1, 4, 0, 0, TimeSpan.Zero));
    public HostAddress Address { get; } = new("localhost", 47250);
    public KnownHosts Known { get; }
    public IChannelFactory Channels { get; }

    /// <summary>While true every request fails as a dropped network does, which gRPC reports as Unavailable.</summary>
    public bool Offline { get; set; }

    public sealed record Request(string Path, string? Version, string? Machine, bool HasToken);

    public ConcurrentQueue<Request> Requests { get; } = new();

    public HostConnection Connection(string? clientVersion = null)
    {
        var c = new HostConnection(Channels, Known, Time, clientVersion ?? ProductVersion.Current, Machine);
        connections.Add(c);
        return c;
    }

    public async Task<HostConnection> LoggedInOwnerAsync()
    {
        var c = Connection();
        var state = await c.ConnectAsync(Address);
        if (state == ConnectionState.NeedsSetup) await c.SetupOwnerAsync("owner", Password);
        else await c.LoginAsync("owner", Password);
        return c;
    }

    public async Task<int> NewCompanyAsync(HostConnection c, string name = "A Traders") =>
        (await c.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = name, State = "27", Registration = "Regular", Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
        }, o))).Company;

    /// <summary>A shell over a connection that is not logged in, with stores read from this PC's files only.</summary>
    public ShellViewModel Shell(IReadOnlyDictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> built, IConfirm confirm)
    {
        var connection = Connection();
        var files = new MachineFiles(new ClientPaths(ClientFolder));
        var ctx = new ShellContext
        {
            Host = connection, Settings = new SettingsStore(files, connection), Keys = new KeybindingStore(files, connection), Time = Time,
            WorkingDate = DateOnly.FromDateTime(Time.GetLocalNow().DateTime),
        };
        ctx.Settings.LoadAsync(null).GetAwaiter().GetResult();
        ctx.Keys.LoadAsync().GetAwaiter().GetResult();
        return new ShellViewModel(ctx, new FunctionRouter(built), confirm);
    }

    /// <summary>Ends every session, as a host restart does.</summary>
    public void EndAllSessions() => factory.Services.GetRequiredService<SessionStore>().RemoveAll();

    string? otherToken;

    /// <summary>A second logged in Owner, for changes made from another PC.</summary>
    public HostClients OtherOwnerClient() => new(factory.Channel().CreateCallInvoker());

    public Metadata Headers()
    {
        otherToken ??= new Contracts.V1.Session.SessionClient(factory.Channel())
            .Login(new Credentials { Name = "owner", Password = Password }, HostFactory.Headers()).Token;
        return HostFactory.Headers(otherToken);
    }

    /// <summary>Waits until the connection's retry work is waiting on the clock or done, so tests never sleep.</summary>
    public async Task Settle(HostConnection c)
    {
        for (var i = 0; i < 500 && c.Busy; i++) await Task.Delay(10);
        Assert.False(c.Busy, "The connection did not settle.");
    }

    public void Dispose()
    {
        foreach (var c in connections) c.DisposeAsync().AsTask().GetAwaiter().GetResult();
        factory.Dispose();
        try { Directory.Delete(ClientFolder, recursive: true); }
        catch (IOException) { }
    }

    sealed class InMemoryChannels(ClientTestHost owner) : IChannelFactory
    {
        public ChannelOpening Open(HostAddress host, string? pinnedFingerprint)
        {
            var handler = new Recording(owner, owner.factory.Server.CreateHandler());
            var channel = GrpcChannel.ForAddress(owner.factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = handler });
            return new ChannelOpening(channel) { SeenFingerprint = FakeFingerprint };
        }
    }

    sealed class Recording(ClientTestHost owner, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (owner.Offline)
                throw new HttpRequestException("The host is offline.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
            owner.Requests.Enqueue(new Request(request.RequestUri!.AbsolutePath, Header(request, "ohk-version"), Header(request, "ohk-machine"),
                request.Headers.Contains("authorization")));
            return base.SendAsync(request, cancellationToken);
        }

        static string? Header(HttpRequestMessage r, string name) => r.Headers.TryGetValues(name, out var v) ? v.Single() : null;
    }
}
