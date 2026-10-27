using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.HostDb;

/// <summary>Host wide settings as dot named keys with JSON values (spec 7).</summary>
public static class HostSettingsRepository
{
    public static string? Get(SqliteConnection c, string key) =>
        Sql.Single(c, "SELECT value FROM host_settings WHERE key = $key", r => r.GetString(0), ("$key", key));

    public static void Set(SqliteTransaction tx, string key, string json) =>
        Sql.Execute(tx, "INSERT INTO host_settings(key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", json));
}
