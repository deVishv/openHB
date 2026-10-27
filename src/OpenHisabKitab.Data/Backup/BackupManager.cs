using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.Backup;

public sealed record BackupFile(string Path, string SourceName, DateTimeOffset TakenAt);

public sealed class BackupFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Backups with SQLite's online backup (spec 9). A copy is kept only if it passes an integrity check, and a restore
/// checks the backup before it touches the live file.
/// </summary>
public sealed class BackupManager(TimeProvider time)
{
    const string Stamp = "yyyyMMdd-HHmmss";

    public BackupFile Backup(string sourcePath, string folder)
    {
        Directory.CreateDirectory(folder);
        var source = Path.GetFileNameWithoutExtension(sourcePath);
        var takenAt = time.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(takenAt, time.LocalTimeZone);
        var baseName = $"{source}-{local.ToString(Stamp, CultureInfo.InvariantCulture)}";
        var path = Path.Combine(folder, baseName + ".db");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{baseName}-{n}.db"));

        try
        {
            using (var from = SqliteDatabase.Open(sourcePath))
            using (var to = OpenUnpooled(path, SqliteOpenMode.ReadWriteCreate))
                from.BackupDatabase(to);
            CheckIntegrity(path);
        }
        catch (Exception e) when (e is SqliteException or BackupFailedException)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            throw e as BackupFailedException ?? new BackupFailedException($"Backup of {source} failed: {e.Message}", e);
        }
        return new BackupFile(path, source, takenAt);
    }

    /// <summary>Backups of one source file, newest first.</summary>
    public IReadOnlyList<BackupFile> List(string folder, string sourceName)
    {
        if (!Directory.Exists(folder)) return [];
        var pattern = new Regex($@"^{Regex.Escape(sourceName)}-(\d{{8}}-\d{{6}})(-(\d+))?\.db$", RegexOptions.CultureInvariant);
        return Directory.EnumerateFiles(folder)
            .Select(p => (Path: p, Match: pattern.Match(Path.GetFileName(p))))
            .Where(x => x.Match.Success)
            .Select(x => (x.Path, At: ParseStamp(x.Match.Groups[1].Value),
                Seq: x.Match.Groups[3].Success ? int.Parse(x.Match.Groups[3].Value, CultureInfo.InvariantCulture) : 1))
            .OrderByDescending(x => x.At).ThenByDescending(x => x.Seq)
            .Select(x => new BackupFile(x.Path, sourceName, x.At))
            .ToList();
    }

    public void Prune(string folder, string sourceName, int keep)
    {
        foreach (var old in List(folder, sourceName).Skip(keep)) File.Delete(old.Path);
    }

    /// <summary>Copies a checked backup over the live database through <paramref name="live"/>. A damaged backup changes nothing.</summary>
    public void RestoreInto(string backupPath, SqliteConnection live)
    {
        try
        {
            CheckIntegrity(backupPath);
            using var from = OpenUnpooled(backupPath, SqliteOpenMode.ReadOnly);
            from.BackupDatabase(live);
        }
        catch (SqliteException e)
        {
            throw new BackupFailedException("Backup file is damaged. Nothing was restored.", e);
        }
    }

    DateTimeOffset ParseStamp(string text)
    {
        var local = DateTime.ParseExact(text, Stamp, CultureInfo.InvariantCulture);
        return new DateTimeOffset(local, time.LocalTimeZone.GetUtcOffset(local));
    }

    static void CheckIntegrity(string path)
    {
        string result;
        try
        {
            using var c = OpenUnpooled(path, SqliteOpenMode.ReadOnly);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            result = cmd.ExecuteScalar() as string ?? "";
        }
        catch (SqliteException e)
        {
            throw new BackupFailedException("Backup file is damaged. Nothing was restored.", e);
        }
        if (result != "ok") throw new BackupFailedException("Backup file is damaged. Nothing was restored.");
    }

    static SqliteConnection OpenUnpooled(string path, SqliteOpenMode mode)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        c.Open();
        return c;
    }
}
