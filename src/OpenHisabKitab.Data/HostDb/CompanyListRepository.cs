using System.Globalization;
using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.HostDb;

public sealed record CompanyListEntry(int Id, string Name, string FileName);

public static class CompanyListRepository
{
    static CompanyListEntry Map(SqliteDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetString(2));

    /// <summary>Adds a company. Its file is named from its id, so a rename never moves a file.</summary>
    public static int Insert(SqliteTransaction tx, string name)
    {
        var id = (int)Sql.InsertReturningId(tx, "INSERT INTO companies(name, file_name) VALUES ($name, '')", ("$name", name));
        Sql.Execute(tx, "UPDATE companies SET file_name = $file WHERE id = $id",
            ("$file", string.Create(CultureInfo.InvariantCulture, $"company-{id}.db")), ("$id", id));
        return id;
    }

    public static IReadOnlyList<CompanyListEntry> List(SqliteConnection c) =>
        Sql.Query(c, "SELECT id, name, file_name FROM companies ORDER BY name", Map);

    public static CompanyListEntry? Get(SqliteConnection c, int id) =>
        Sql.Single(c, "SELECT id, name, file_name FROM companies WHERE id = $id", Map, ("$id", id));

    public static void Rename(SqliteTransaction tx, int id, string name) =>
        Sql.Execute(tx, "UPDATE companies SET name = $name WHERE id = $id", ("$name", name), ("$id", id));
}
