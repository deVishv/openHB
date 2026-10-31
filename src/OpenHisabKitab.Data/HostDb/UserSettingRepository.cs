using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.HostDb;

/// <summary>Each user's own settings as dot named keys with JSON values (spec 7, the user layer).</summary>
public static class UserSettingRepository
{
    public static IReadOnlyDictionary<string, string> List(SqliteConnection c, int userId) =>
        Sql.Query(c, "SELECT key, value FROM user_settings WHERE user_id = $user ORDER BY key", r => (r.GetString(0), r.GetString(1)), ("$user", userId))
            .ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);

    public static string? Get(SqliteConnection c, int userId, string key) =>
        Sql.Single(c, "SELECT value FROM user_settings WHERE user_id = $user AND key = $key", r => r.GetString(0), ("$user", userId), ("$key", key));

    public static void Set(SqliteTransaction tx, int userId, string key, string json) =>
        Sql.Execute(tx, "INSERT INTO user_settings(user_id, key, value) VALUES ($user, $key, $value) ON CONFLICT(user_id, key) DO UPDATE SET value = excluded.value",
            ("$user", userId), ("$key", key), ("$value", json));

    public static bool Remove(SqliteTransaction tx, int userId, string key) =>
        Sql.Execute(tx, "DELETE FROM user_settings WHERE user_id = $user AND key = $key", ("$user", userId), ("$key", key)) > 0;
}
