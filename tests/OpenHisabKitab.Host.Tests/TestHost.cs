namespace OpenHisabKitab.Host.Tests;

/// <summary>The application services over a temporary data folder and a fake clock.</summary>
public sealed class TestHost : IDisposable
{
    public const string OwnerPassword = "owner-pass-1";

    public TestHost()
    {
        Folder = Path.Combine(Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 4, 0, 0, TimeSpan.Zero));
        Data = new HostData(Folder);
        Sessions = new SessionStore(Time);
        Auth = new AuthService(Data, Sessions, new LoginThrottle(Time), Time);
        Users = new UserService(Data, Sessions, Time);
    }

    public string Folder { get; }
    public FakeTimeProvider Time { get; }
    public HostData Data { get; }
    public SessionStore Sessions { get; }
    public AuthService Auth { get; }
    public UserService Users { get; }

    Session? owner;

    /// <summary>Sets up the Owner on first use and returns the Owner's session.</summary>
    public Session Owner => owner ??= Auth.SetupOwner("owner", OwnerPassword, "HOSTPC", isLoopback: true);

    /// <summary>Creates a user with the role and logs them in.</summary>
    public Session NewUser(string name, BuiltInRole role, params int[] companies)
    {
        var id = Users.Create(Owner.Context, name, "password-" + name, role);
        if (companies.Length > 0) Users.SetCompanies(Owner.Context, id, companies);
        return Auth.Login(name, "password-" + name, "PC2");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
