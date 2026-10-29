using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class ApiTokenRepositoryTests : IDisposable
{
    readonly TempDb db = new("host.db");
    readonly SqliteConnection c;

    public ApiTokenRepositoryTests()
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

    [Fact]
    public void Insert_find_list_revoke_touch()
    {
        var at = new DateTimeOffset(2026, 10, 29, 4, 0, 0, TimeSpan.Zero);
        var user = Write(tx => UserRepository.Insert(tx, "owner", "hash", BuiltInRole.Owner));
        var first = Write(tx => ApiTokenRepository.Insert(tx, user, "scripts", "h1", readWrite: false, at));
        var second = Write(tx => ApiTokenRepository.Insert(tx, user, "claude", "h2", readWrite: true, at.AddMinutes(1)));

        var found = ApiTokenRepository.FindByHash(c, "h2");
        Assert.NotNull(found);
        Assert.Equal(new ApiTokenRecord(second, user, "claude", true, at.AddMinutes(1), null, null), found.Value.Token);
        Assert.Equal("h2", found.Value.Hash);
        Assert.Null(ApiTokenRepository.FindByHash(c, "unknown"));

        Assert.Equal([second, first], ApiTokenRepository.ListForUser(c, user).Select(t => t.Id));

        ApiTokenRepository.Touch(c, first, at.AddHours(1));
        Assert.Equal(at.AddHours(1), ApiTokenRepository.Get(c, first)!.LastUsedAt);

        Assert.True(Write(tx => ApiTokenRepository.Revoke(tx, first, at.AddHours(2))));
        Assert.Equal(at.AddHours(2), ApiTokenRepository.Get(c, first)!.RevokedAt);
        Assert.False(Write(tx => ApiTokenRepository.Revoke(tx, first, at.AddHours(3))));
        Assert.Null(ApiTokenRepository.Get(c, 999));
    }
}
