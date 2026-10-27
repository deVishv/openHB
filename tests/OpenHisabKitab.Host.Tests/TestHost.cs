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
        Feed = new ChangeFeed();
        Hub = new CompanyHub(Data, Time);
        Companies = new CompanyService(Data, Hub, Feed, Sessions, Time);
        Masters = new MasterService(Hub, Feed, Observer, Time);
        Vouchers = new VoucherService(Hub, Feed, Observer, Time);
    }

    public string Folder { get; }
    public FakeTimeProvider Time { get; }
    public HostData Data { get; }
    public SessionStore Sessions { get; }
    public AuthService Auth { get; }
    public UserService Users { get; }
    public ChangeFeed Feed { get; }
    public CompanyHub Hub { get; }
    public CompanyService Companies { get; }
    public MasterService Masters { get; }
    public VoucherService Vouchers { get; }
    public RecordingObserver Observer { get; } = new();

    public static readonly GstData Gst = GstData.LoadEmbedded();

    public static Gstin G(string text) => Gstin.TryParse(text, Gst, out var g) ? g : throw new ArgumentException(text);

    public static CompanyProfile Profile(string name = "A Traders") =>
        new(name, new StateCode("27"), GstRegistration.Regular, G("27AAPFU0939F1ZV"), "1 MG Road, Pune", 2026);

    /// <summary>Creates a Regular company in Maharashtra (27) with books from FY 2026-27, as the Owner.</summary>
    public int NewCompany(CompanyProfile? profile = null) => Companies.Create(Owner.Context, profile ?? Profile());

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

/// <summary>Records what the services report to the books cache.</summary>
public sealed class RecordingObserver : IBooksObserver
{
    public List<(int Company, long Voucher, IReadOnlyList<PostingRecord> Postings)> Saved { get; } = [];
    public List<int> MastersChanged { get; } = [];

    public void VoucherSaved(int company, long voucherId, IReadOnlyList<PostingRecord> postings) => Saved.Add((company, voucherId, postings));

    public void ChartChanged(int company) => MastersChanged.Add(company);
}
