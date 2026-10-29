using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.HostDb;

/// <summary>An API token of a user (spec 4.1). The token itself is never stored, only its hash.</summary>
public sealed record ApiTokenRecord(
    int Id, int UserId, string Name, bool ReadWrite, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt);

public static class ApiTokenRepository
{
    const string Columns = "id, user_id, name, read_write, created_at, last_used_at, revoked_at, token_hash";

    static ApiTokenRecord Map(SqliteDataReader r) =>
        new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetInt64(3) != 0, ParseAt(r.GetString(4)),
            r.NullableString(5) is { } used ? ParseAt(used) : null, r.NullableString(6) is { } revoked ? ParseAt(revoked) : null);

    public static int Insert(SqliteTransaction tx, int userId, string name, string tokenHash, bool readWrite, DateTimeOffset at) =>
        (int)Sql.InsertReturningId(tx, """
            INSERT INTO api_tokens(user_id, name, token_hash, read_write, created_at)
            VALUES ($user, $name, $hash, $rw, $at)
            """, ("$user", userId), ("$name", name), ("$hash", tokenHash), ("$rw", readWrite ? 1 : 0), ("$at", FormatAt(at)));

    public static (ApiTokenRecord Token, string Hash)? FindByHash(SqliteConnection c, string tokenHash) =>
        Sql.Query(c, $"SELECT {Columns} FROM api_tokens WHERE token_hash = $hash", r => (Map(r), r.GetString(7)), ("$hash", tokenHash))
            is [var found] ? found : null;

    /// <summary>The user's tokens, newest first, revoked ones included.</summary>
    public static IReadOnlyList<ApiTokenRecord> ListForUser(SqliteConnection c, int userId) =>
        Sql.Query(c, $"SELECT {Columns} FROM api_tokens WHERE user_id = $user ORDER BY id DESC", Map, ("$user", userId));

    public static ApiTokenRecord? Get(SqliteConnection c, int id) =>
        Sql.Single(c, $"SELECT {Columns} FROM api_tokens WHERE id = $id", Map, ("$id", id));

    /// <summary>False when the token was already revoked.</summary>
    public static bool Revoke(SqliteTransaction tx, int id, DateTimeOffset at) =>
        Sql.Execute(tx, "UPDATE api_tokens SET revoked_at = $at WHERE id = $id AND revoked_at IS NULL", ("$at", FormatAt(at)), ("$id", id)) == 1;

    /// <summary>Records use outside any transaction, so reading with a token never waits for the host write lock.</summary>
    public static void Touch(SqliteConnection c, int id, DateTimeOffset at)
    {
        using var cmd = Sql.Command(c, "UPDATE api_tokens SET last_used_at = $at WHERE id = $id", ("$at", FormatAt(at)), ("$id", id));
        cmd.ExecuteNonQuery();
    }

    static string FormatAt(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    static DateTimeOffset ParseAt(string text) => DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture);
}
