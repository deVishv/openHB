namespace OpenHisabKitab.Data.Tests;

/// <summary>A database file in its own temporary folder, deleted on dispose.</summary>
public sealed class TempDb : IDisposable
{
    public TempDb(string name = "test.db")
    {
        Folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ohk-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Path = System.IO.Path.Combine(Folder, name);
    }

    public string Folder { get; }

    public string Path { get; }

    public SqliteConnection Open(int busySeconds = 5) => SqliteDatabase.Open(Path, busySeconds);

    public static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}
