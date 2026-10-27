using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.HostDb;

public enum BuiltInRole { Owner, Accountant, DataEntry, Viewer }

public sealed record UserRecord(int Id, string Name, string PasswordHash, BuiltInRole Role, bool Active, long RowVersion);

public static class UserRepository
{
    const string Columns = "id, name, password_hash, role, active, row_version";

    static UserRecord Map(SqliteDataReader r) =>
        new(r.GetInt32(0), r.GetString(1), r.GetString(2), Enum.Parse<BuiltInRole>(r.GetString(3)), r.GetInt64(4) != 0, r.GetInt64(5));

    public static int Count(SqliteConnection c) => Sql.Query(c, "SELECT count(*) FROM users", r => r.GetInt32(0))[0];

    public static UserRecord? Find(SqliteConnection c, string name) =>
        Sql.Single(c, $"SELECT {Columns} FROM users WHERE name = $name", Map, ("$name", name));

    public static UserRecord? Get(SqliteConnection c, int id) =>
        Sql.Single(c, $"SELECT {Columns} FROM users WHERE id = $id", Map, ("$id", id));

    public static IReadOnlyList<UserRecord> List(SqliteConnection c) =>
        Sql.Query(c, $"SELECT {Columns} FROM users ORDER BY name", Map);

    public static int Insert(SqliteTransaction tx, string name, string hash, BuiltInRole role) =>
        (int)Sql.InsertReturningId(tx, "INSERT INTO users(name, password_hash, role) VALUES ($name, $hash, $role)",
            ("$name", name), ("$hash", hash), ("$role", role.ToString()));

    public static void SetPassword(SqliteTransaction tx, int id, string hash) =>
        Sql.Execute(tx, "UPDATE users SET password_hash = $hash, row_version = row_version + 1 WHERE id = $id",
            ("$hash", hash), ("$id", id));

    public static void SetCompanies(SqliteTransaction tx, int userId, IReadOnlyList<int> companyIds)
    {
        Sql.Execute(tx, "DELETE FROM user_companies WHERE user_id = $u", ("$u", userId));
        foreach (var id in companyIds.Distinct())
            Sql.Execute(tx, "INSERT INTO user_companies(user_id, company_id) VALUES ($u, $c)", ("$u", userId), ("$c", id));
    }

    public static IReadOnlyList<int> Companies(SqliteConnection c, int userId) =>
        Sql.Query(c, "SELECT company_id FROM user_companies WHERE user_id = $u ORDER BY company_id", r => r.GetInt32(0), ("$u", userId));
}
