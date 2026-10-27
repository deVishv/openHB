using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data;

/// <summary>Small helpers so repositories read as SQL plus parameters.</summary>
internal static class Sql
{
    public static SqliteCommand Command(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public static SqliteCommand Command(SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = Command(tx.Connection!, sql, args);
        cmd.Transaction = tx;
        return cmd;
    }

    public static int Execute(SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(tx, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public static long InsertReturningId(SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(tx, sql + " RETURNING id", args);
        return (long)cmd.ExecuteScalar()!;
    }

    public static List<T> Query<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public static T? Single<T>(SqliteConnection c, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
        where T : class =>
        Query(c, sql, map, args).SingleOrDefault();

    public static string? NullableString(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
