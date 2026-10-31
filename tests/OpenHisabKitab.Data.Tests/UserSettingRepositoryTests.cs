using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class UserSettingRepositoryTests : IDisposable
{
    readonly TempDb db = new("host.db");
    readonly SqliteConnection c;

    public UserSettingRepositoryTests()
    {
        c = db.Open();
        Migrator.Migrate(c, Schema.Host);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO users(id, name, password_hash, role) VALUES (1, 'owner', 'x', 'Owner'), (2, 'clerk', 'x', 'DataEntry')";
        cmd.ExecuteNonQuery();
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
    public void Set_get_list_remove()
    {
        Write(tx => { UserSettingRepository.Set(tx, 1, "ui.fontSize", "20"); return 0; });
        Write(tx => { UserSettingRepository.Set(tx, 1, "ui.fontSize", "22"); return 0; });
        Write(tx => { UserSettingRepository.Set(tx, 2, "ui.fontSize", "16"); return 0; });
        Assert.Equal("22", UserSettingRepository.Get(c, 1, "ui.fontSize"));
        Assert.Equal(new Dictionary<string, string> { ["ui.fontSize"] = "16" }, UserSettingRepository.List(c, 2));
        Assert.True(Write(tx => UserSettingRepository.Remove(tx, 1, "ui.fontSize")));
        Assert.False(Write(tx => UserSettingRepository.Remove(tx, 1, "ui.fontSize")));
        Assert.Null(UserSettingRepository.Get(c, 1, "ui.fontSize"));
        Assert.Empty(UserSettingRepository.List(c, 1));
    }
}
