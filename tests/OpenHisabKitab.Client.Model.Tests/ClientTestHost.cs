using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Tests.Shell;
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
    readonly List<ShellViewModel> shells = [];

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

    /// <summary>When set, the Nth request (1-based) whose path ends with the text fails as a dropped link does.</summary>
    public (string Path, int Nth)? FailRequest { get; set; }

    int failCount;

    /// <summary>While set, Companies.List requests wait for it, so a test can act while one is in flight.</summary>
    public TaskCompletionSource? HoldCompanyList { get; set; }

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
    public ShellViewModel Shell(IReadOnlyDictionary<string, ScreenFactory> built, IConfirm confirm)
    {
        var connection = Connection();
        var files = new MachineFiles(new ClientPaths(ClientFolder));
        var ctx = new ShellContext
        {
            Host = connection, Files = files, Settings = new SettingsStore(files, connection), Keys = new KeybindingStore(files, connection), Time = Time,
            WorkingDate = DateOnly.FromDateTime(Time.GetLocalNow().DateTime),
        };
        ctx.Settings.LoadAsync(null).GetAwaiter().GetResult();
        ctx.Keys.LoadAsync().GetAwaiter().GetResult();
        var shell = new ShellViewModel(ctx, new FunctionRouter(built), confirm);
        shells.Add(shell);
        shell.Start();
        return shell;
    }

    /// <summary>A shell whose screens are fakes for HELP, CMPY, SETG, DATE; each load makes one host call, as real screens do.</summary>
    public ShellViewModel Shell(IConfirm? confirm = null)
    {
        ScreenFactory Fake(string code) => (cmd, ctx) => Task.FromResult<IScreen?>(
            new FakeScreen(code, subject: string.Join(' ', cmd.Args),
                onLoad: () => ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Contracts.V1.Empty(), o))));
        return Shell(new[] { "HELP", "CMPY", "SETG", "DATE" }.ToDictionary(c => c, Fake), confirm ?? new FakeConfirm(true));
    }

    /// <summary>A logged in Owner's shell with tabs open for the codes given, and company "A Traders" open when named.</summary>
    public async Task<ShellViewModel> LoggedInShellAsync(string[] open, string? company = null,
        IReadOnlyDictionary<string, ScreenFactory>? built = null, IConfirm? confirm = null)
    {
        var shell = built is null ? Shell(confirm) : Shell(built, confirm ?? new FakeConfirm(true));
        var feed = factory.Services.GetRequiredService<ChangeFeed>();
        var watching = feed.SubscriberCount;
        shell.Session.HostName = Address.Name;
        shell.Session.Port = Address.Port.ToString(CultureInfo.InvariantCulture);
        await shell.Session.ConnectAsync();
        shell.Session.UserName = "owner";
        if (shell.Context.Host.State == ConnectionState.NeedsSetup) await shell.Session.SetupAsync(Password, Password);
        else await shell.Session.LoginAsync(Password);
        while (shell.Tabs.Tabs.Count > 0) await shell.Tabs.CloseActiveAsync(new FakeConfirm(true));
        if (company is not null)
        {
            var id = await NewCompanyAsync(shell.Context.Host, company);
            await CompanyOpener.OpenAsync(shell.Context, new Contracts.V1.CompanyEntry { Id = id, Name = company });
        }
        foreach (var code in open) await shell.RunAsync(code, newTab: true);
        await Eventually(() => feed.SubscriberCount > watching);   // the change stream is live
        return shell;
    }

    /// <summary>A shell logged in as a new user with the role and companies given, created by the Owner.</summary>
    public async Task<ShellViewModel> UserShellAsync(string name, string role, params int[] companies)
    {
        var admin = OtherOwnerClient().Admin;
        var id = (int)(await admin.CreateUserAsync(new CreateUserRequest { Name = name, Password = Password, Role = role }, Headers())).Id;
        await admin.SetUserCompaniesAsync(new SetUserCompaniesRequest { UserId = id, Companies = { companies } }, Headers());
        var shell = Shell();
        shell.Session.HostName = Address.Name;
        await shell.Session.ConnectAsync();
        shell.Session.UserName = name;
        await shell.Session.LoginAsync(Password);
        while (shell.Tabs.Tabs.Count > 0) await shell.Tabs.CloseActiveAsync(new FakeConfirm(true));
        return shell;
    }

    public static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "The expected state did not arrive.");
    }

    /// <summary>Waits for something the change stream delivers in the background.</summary>
    public async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "The expected change did not arrive.");
    }

    /// <summary>The host's token service, to prove a token made in USER works on the API.</summary>
    public ApiTokenService ApiTokens => factory.Services.GetRequiredService<ApiTokenService>();

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
        foreach (var s in shells) s.DisposeAsync().AsTask().GetAwaiter().GetResult();
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
            if (owner.FailRequest is { } fail && request.RequestUri!.AbsolutePath.EndsWith(fail.Path, StringComparison.Ordinal)
                && Interlocked.Increment(ref owner.failCount) == fail.Nth)
                throw new HttpRequestException("Dropped.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionReset));
            if (owner.Offline)
                throw new HttpRequestException("The host is offline.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
            owner.Requests.Enqueue(new Request(request.RequestUri!.AbsolutePath, Header(request, "ohk-version"), Header(request, "ohk-machine"),
                request.Headers.Contains("authorization")));
            return request.RequestUri.AbsolutePath.EndsWith("Companies/List", StringComparison.Ordinal) && owner.HoldCompanyList is { } hold
                ? SendLaterAsync(hold.Task, request, cancellationToken)
                : base.SendAsync(request, cancellationToken);
        }

        async Task<HttpResponseMessage> SendLaterAsync(Task hold, HttpRequestMessage request, CancellationToken ct)
        {
            await hold.WaitAsync(ct);
            return await base.SendAsync(request, ct);
        }

        static string? Header(HttpRequestMessage r, string name) => r.Headers.TryGetValues(name, out var v) ? v.Single() : null;
    }
}
