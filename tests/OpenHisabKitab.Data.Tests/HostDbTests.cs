using OpenHisabKitab.Data.HostDb;
using static OpenHisabKitab.Data.Tests.TempDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class HostDbTests : IDisposable
{
    readonly TempDb db = new("host.db");
    readonly SqliteConnection c;

    public HostDbTests()
    {
        c = db.Open();
        Migrator.Migrate(c, Schema.Host);
    }

    public void Dispose()
    {
        c.Dispose();
        db.Dispose();
    }

    T Write<T>(Func<SqliteTransaction, T> work)
    {
        using var tx = SqliteDatabase.BeginWrite(c);
        var result = work(tx);
        tx.Commit();
        return result;
    }

    void Write(Action<SqliteTransaction> work) => Write(tx => { work(tx); return 0; });

    [Fact]
    public void Insert_then_find_ignores_case()
    {
        var id = Write(tx => UserRepository.Insert(tx, "owner", "hash", BuiltInRole.Owner));
        var u = UserRepository.Find(c, "OWNER");
        Assert.NotNull(u);
        Assert.Equal(new UserRecord(id, "owner", "hash", BuiltInRole.Owner, true, 1), u);
        Assert.Equal(1, UserRepository.Count(c));
        Assert.Equal(u, UserRepository.Get(c, id));
        Assert.Null(UserRepository.Find(c, "nobody"));
    }

    [Fact]
    public void Duplicate_name_in_other_case_throws()
    {
        Write(tx => UserRepository.Insert(tx, "owner", "h", BuiltInRole.Owner));
        Assert.Throws<SqliteException>(() => Write(tx => UserRepository.Insert(tx, "Owner", "h", BuiltInRole.Viewer)));
    }

    [Fact]
    public void Set_password_bumps_row_version()
    {
        var id = Write(tx => UserRepository.Insert(tx, "owner", "h1", BuiltInRole.Owner));
        Write(tx => UserRepository.SetPassword(tx, id, "h2"));
        var u = UserRepository.Get(c, id)!;
        Assert.Equal("h2", u.PasswordHash);
        Assert.Equal(2, u.RowVersion);
    }

    [Fact]
    public void List_returns_users_by_name()
    {
        Write(tx => UserRepository.Insert(tx, "zara", "h", BuiltInRole.Viewer));
        Write(tx => UserRepository.Insert(tx, "asha", "h", BuiltInRole.DataEntry));
        Assert.Equal(["asha", "zara"], UserRepository.List(c).Select(u => u.Name));
    }

    [Fact]
    public void Set_companies_replaces_previous_set()
    {
        var u = Write(tx => UserRepository.Insert(tx, "asha", "h", BuiltInRole.DataEntry));
        var a = Write(tx => CompanyListRepository.Insert(tx, "A Traders"));
        var b = Write(tx => CompanyListRepository.Insert(tx, "B Traders"));
        Write(tx => UserRepository.SetCompanies(tx, u, [a, b]));
        Write(tx => UserRepository.SetCompanies(tx, u, [b]));
        Assert.Equal([b], UserRepository.Companies(c, u));
    }

    [Fact]
    public void Unknown_company_access_throws()
    {
        var u = Write(tx => UserRepository.Insert(tx, "asha", "h", BuiltInRole.DataEntry));
        Assert.Throws<SqliteException>(() => Write(tx => UserRepository.SetCompanies(tx, u, [42])));
    }

    [Fact]
    public void First_company_file_name()
    {
        var id = Write(tx => CompanyListRepository.Insert(tx, "A Traders"));
        Assert.Equal(new CompanyListEntry(id, "A Traders", $"company-{id}.db"), CompanyListRepository.Get(c, id));
        Assert.Equal("company-1.db", CompanyListRepository.List(c).Single().FileName);
    }

    [Fact]
    public void Rename_company()
    {
        var id = Write(tx => CompanyListRepository.Insert(tx, "A Traders"));
        Write(tx => CompanyListRepository.Rename(tx, id, "A Traders LLP"));
        Assert.Equal("A Traders LLP", CompanyListRepository.Get(c, id)!.Name);
        Assert.Null(CompanyListRepository.Get(c, 99));
    }

    [Fact]
    public void Setting_round_trip_and_overwrite()
    {
        Assert.Null(HostSettingsRepository.Get(c, "api.enabled"));
        Write(tx => HostSettingsRepository.Set(tx, "api.enabled", "false"));
        Write(tx => HostSettingsRepository.Set(tx, "api.enabled", "true"));
        Assert.Equal("true", HostSettingsRepository.Get(c, "api.enabled"));
    }

    [Fact]
    public void Api_tokens_table_exists() =>
        Assert.Equal(1L, Scalar(c, "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = 'api_tokens'"));
}
