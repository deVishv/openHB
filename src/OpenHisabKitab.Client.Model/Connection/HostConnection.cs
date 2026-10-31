using System.Globalization;
using Grpc.Core;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>
/// The client's one link to the host: trust on first use, the version check, login, and every call after it (spec 4 and
/// spec 13). A dropped link is retried on its own while open edits stay on screen; a session the host no longer knows,
/// as after a host restart, asks for login without closing anything.
/// </summary>
public sealed class HostConnection(IChannelFactory channels, KnownHosts known, TimeProvider time, string clientVersion, string machineName)
    : IAsyncDisposable
{
    static readonly TimeSpan[] RetryDelays = [.. new[] { 1, 2, 4, 8, 15, 30 }.Select(s => TimeSpan.FromSeconds(s))];

    readonly CancellationTokenSource stopping = new();
    readonly Lock gate = new();
    ChannelOpening? opening;
    HostClients? clients;
    Task? retry;
    Task? retryDelay;
    int retryGeneration;
    bool sessionLost;

    public ConnectionState State { get; private set; }

    public event Action<ConnectionState>? StateChanged;

    /// <summary>Raised when a lost connection or session comes back logged in: change events may have been missed meanwhile.</summary>
    public event Action? Reconnected;

    public HostAddress? Address { get; private set; }

    public string? SeenFingerprint => opening?.SeenFingerprint;

    public string? PinnedFingerprint { get; private set; }

    public LoginReply? Session { get; private set; }

    /// <summary>The one line explaining <see cref="State"/>, or null.</summary>
    public string? Message { get; private set; }

    /// <summary>How long a unary call may take. Tests shorten it.</summary>
    internal TimeSpan CallDeadline { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>True while retry work is running rather than waiting on the clock. Tests wait for it to settle.</summary>
    internal bool Busy
    {
        get
        {
            lock (gate) return retry is { IsCompleted: false } && retryDelay is null or { IsCompleted: true };
        }
    }

    public async Task<ConnectionState> ConnectAsync(HostAddress host, CancellationToken ct = default)
    {
        StopRetry();
        // Connecting again from a session, as after accepting a changed certificate, misses changes like a lost session.
        var hadSession = Session is not null || sessionLost;
        opening?.Dispose();
        Address = host;
        PinnedFingerprint = known.Fingerprint(host);
        opening = channels.Open(host, PinnedFingerprint);
        clients = new HostClients(opening.Channel.CreateCallInvoker());
        Session = null;
        sessionLost = hadSession;
        Set(ConnectionState.Connecting, null);

        HostInfo info;
        try { info = await clients.Session.GetInfoAsync(new Empty(), Options(ct, token: false)); }
        catch (RpcException) when (CertificateNotTrusted())
        {
            Set(PinnedFingerprint is null ? ConnectionState.NeedsTrust : ConnectionState.CertificateChanged, null);
            return State;
        }
        catch (RpcException)
        {
            Set(ConnectionState.Disconnected, Text.HostNotFound);
            return State;
        }
        if (info.Version != clientVersion)
            Set(ConnectionState.VersionMismatch, string.Format(CultureInfo.InvariantCulture, Text.VersionMismatch, info.Version));
        else Set(info.NeedsSetup ? ConnectionState.NeedsSetup : ConnectionState.NeedsLogin, null);
        return State;
    }

    /// <summary>
    /// Pins the certificate the user was shown, then connects again. If the host now shows another one, nothing is
    /// pinned and the connect shows the new one for the user to check.
    /// </summary>
    public Task<ConnectionState> TrustSeenCertificateAsync(string shownFingerprint, CancellationToken ct = default)
    {
        if (Address is null || SeenFingerprint is null) throw new InvalidOperationException("No certificate has been seen.");
        if (shownFingerprint == SeenFingerprint) known.Trust(Address, shownFingerprint);
        return ConnectAsync(Address, ct);
    }

    public Task SetupOwnerAsync(string name, string password, CancellationToken ct = default) =>
        SignInAsync(c => c.Session.SetupOwnerAsync(new Credentials { Name = name, Password = password }, Options(ct, token: false, machine: true)));

    public Task LoginAsync(string name, string password, CancellationToken ct = default) =>
        SignInAsync(c => c.Session.LoginAsync(new Credentials { Name = name, Password = password }, Options(ct, token: false, machine: true)));

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        StopRetry();
        try { if (clients is not null && Session is not null) await clients.Session.LogoutAsync(new Empty(), Options(ct)); }
        catch (RpcException) { }    // the session ends on this side whatever the host answers
        Session = null;
        sessionLost = false;
        Set(ConnectionState.NeedsLogin, null);
    }

    public async Task<T> CallAsync<T>(Func<HostClients, CallOptions, AsyncUnaryCall<T>> call, CancellationToken ct = default)
    {
        var c = clients ?? throw new InvalidOperationException("Connect first.");
        try { return await call(c, Options(ct)); }
        catch (RpcException e) when (e.StatusCode == StatusCode.Cancelled && ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (RpcException e)
        {
            var error = HostException.From(e);
            switch (error.Kind)
            {
                case HostErrorKind.Unreachable when Session is not null: StartRetry(); break;
                case HostErrorKind.NeedsLogin:
                    if (Session is not null) sessionLost = true;
                    Set(ConnectionState.NeedsLogin, error.Message);
                    break;
                case HostErrorKind.VersionMismatch: Set(ConnectionState.VersionMismatch, error.Message); break;
            }
            throw error;
        }
    }

    /// <summary>The change stream (spec 4, Transport). It runs until cancelled or the link drops.</summary>
    public AsyncServerStreamingCall<ChangeEventMsg> Watch(CancellationToken ct) =>
        (clients ?? throw new InvalidOperationException("Connect first.")).Changes.Watch(new Empty(), StreamOptions(ct));

    /// <summary>Headers for a server stream such as Changes.Watch: no deadline.</summary>
    public CallOptions StreamOptions(CancellationToken ct) => new(Headers(token: true, machine: false), cancellationToken: ct);

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        if (retry is { } r)
            try { await r; }
            catch (OperationCanceledException) { }
        opening?.Dispose();
        stopping.Dispose();
    }

    async Task SignInAsync(Func<HostClients, AsyncUnaryCall<LoginReply>> call)
    {
        // Credentials go only to a host whose certificate is trusted and whose version matches.
        if (State is not (ConnectionState.NeedsSetup or ConnectionState.NeedsLogin) || clients is null)
            throw new InvalidOperationException("Connect to a trusted host first.");
        LoginReply reply;
        try { reply = await call(clients); }
        catch (RpcException e) { throw HostException.From(e); }
        Session = reply;
        Set(ConnectionState.Connected, null);
        if (sessionLost)
        {
            sessionLost = false;
            Reconnected?.Invoke();
        }
    }

    bool CertificateNotTrusted() => SeenFingerprint is { } seen && seen != PinnedFingerprint;

    CallOptions Options(CancellationToken ct, bool token = true, bool machine = false) =>
        // gRPC checks deadlines against the system clock, so this one never comes from the injected TimeProvider.
        new(Headers(token, machine), DateTime.UtcNow + CallDeadline, ct);

    Metadata Headers(bool token, bool machine)
    {
        var m = new Metadata { { "ohk-version", clientVersion } };
        if (machine) m.Add("ohk-machine", machineName);
        if (token && Session is { } s) m.Add("authorization", "Bearer " + s.Token);
        return m;
    }

    void Set(ConnectionState state, string? message)
    {
        Message = message;
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    void StartRetry()
    {
        lock (gate)
        {
            if (retry is { IsCompleted: false }) return;
            Set(ConnectionState.Reconnecting, Text.HostUnreachable);
            retry = RetryAsync(++retryGeneration, stopping.Token);
        }
    }

    void StopRetry()
    {
        // A new connect or a logout supersedes the loop; it ends at its next check.
        lock (gate)
        {
            retryGeneration++;
            retry = null;
        }
    }

    async Task RetryAsync(int generation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            Task delay;
            lock (gate) delay = retryDelay = Task.Delay(RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)], time, ct);
            try { await delay.ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            lock (gate) if (generation != retryGeneration) return;
            if (await AttemptAsync(generation, ct).ConfigureAwait(false)) return;
        }
    }

    bool Superseded(int generation)
    {
        lock (gate) return generation != retryGeneration;
    }

    /// <summary>
    /// True when the loop is done: back in, logged out, or blocked. An attempt superseded while in flight, by a logout or
    /// a new connect, changes nothing.
    /// </summary>
    async Task<bool> AttemptAsync(int generation, CancellationToken ct)
    {
        var c = clients!;
        HostInfo info;
        try { info = await c.Session.GetInfoAsync(new Empty(), Options(ct, token: false)).ConfigureAwait(false); }
        catch (RpcException)
        {
            if (Superseded(generation)) return true;
            if (!CertificateNotTrusted()) return false;
            Set(ConnectionState.CertificateChanged, null);
            return true;
        }
        if (Superseded(generation)) return true;
        if (info.Version != clientVersion)
        {
            Set(ConnectionState.VersionMismatch, string.Format(CultureInfo.InvariantCulture, Text.VersionMismatch, info.Version));
            return true;
        }
        try
        {
            await c.Companies.ListAsync(new Empty(), Options(ct)).ConfigureAwait(false);
            if (Superseded(generation)) return true;
            Set(ConnectionState.Connected, null);
            Reconnected?.Invoke();
            return true;
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unauthenticated)
        {
            if (Superseded(generation)) return true;
            sessionLost = true;
            Set(ConnectionState.NeedsLogin, e.Status.Detail);
            return true;
        }
        catch (RpcException) { return false; }
    }
}
