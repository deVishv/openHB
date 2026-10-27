using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.Migrations;

public enum Schema { Host, Company }

public sealed record MigrationResult(int From, int To);

public sealed class DatabaseTooNewException() : Exception("This file needs a newer Open Hisab Kitab.");

/// <summary>Runs numbered SQL scripts. The schema version is SQLite's user_version, set inside each script's transaction.</summary>
public static class Migrator
{
    static readonly Dictionary<Schema, IReadOnlyList<string>> Scripts = new()
    {
        [Schema.Host] = LoadScripts(Schema.Host),
        [Schema.Company] = LoadScripts(Schema.Company),
    };

    public static int Latest(Schema s) => Scripts[s].Count;

    /// <summary>
    /// Refuses a file written by a newer version without changing it: the version is read on a read only connection,
    /// before the journal mode pragma could rewrite the file header. A missing file passes.
    /// </summary>
    public static void CheckNotTooNew(string path, Schema s)
    {
        if (!File.Exists(path)) return;
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using var c = new SqliteConnection(builder.ToString());
        c.Open();
        if (UserVersion(c) > Scripts[s].Count) throw new DatabaseTooNewException();
    }

    public static MigrationResult Migrate(SqliteConnection c, Schema s, Action<int>? beforeMigrating = null)
    {
        var scripts = Scripts[s];
        var from = UserVersion(c);
        if (from > scripts.Count) throw new DatabaseTooNewException();
        if (from == scripts.Count) return new MigrationResult(from, from);
        if (from > 0) beforeMigrating?.Invoke(from);

        for (var v = from; v < scripts.Count; v++)
        {
            using var tx = SqliteDatabase.BeginWrite(c);
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = scripts[v] + string.Create(CultureInfo.InvariantCulture, $"\nPRAGMA user_version = {v + 1};");
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        return new MigrationResult(from, scripts.Count);
    }

    static int UserVersion(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static IReadOnlyList<string> LoadScripts(Schema s)
    {
        var asm = Assembly.GetExecutingAssembly();
        var prefix = $"Migrations.{s}.";
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var scripts = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var expected = string.Create(CultureInfo.InvariantCulture, $"{prefix}{i + 1:D4}_");
            if (!names[i].StartsWith(expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Migration {i + 1} for {s} is missing or misnamed: {names[i]}");
            using var stream = asm.GetManifestResourceStream(names[i])!;
            using var reader = new StreamReader(stream);
            scripts.Add(reader.ReadToEnd());
        }
        return scripts;
    }
}
