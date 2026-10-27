using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Data.CompanyDb;

public sealed record Item(int Id, string Name, string HsnSac, bool IsService, string Unit, GstRate Rate, GstRate CessRate, Money DefaultPrice, long RowVersion);

public static class ItemRepository
{
    const string Columns = "id, name, hsn_sac, is_service, unit, rate_bp, cess_bp, default_price_paise, row_version";

    static Item Map(SqliteDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0, r.GetString(4),
        new GstRate(r.GetInt32(5)), new GstRate(r.GetInt32(6)), new Money(r.GetInt64(7)), r.GetInt64(8));

    public static Item? Get(SqliteConnection c, int id) =>
        Sql.Single(c, $"SELECT {Columns} FROM items WHERE id = $id", Map, ("$id", id));

    public static IReadOnlyList<Item> List(SqliteConnection c) =>
        Sql.Query(c, $"SELECT {Columns} FROM items ORDER BY name", Map);

    public static int Insert(SqliteTransaction tx, Item i) =>
        (int)Sql.InsertReturningId(tx, """
            INSERT INTO items(name, hsn_sac, is_service, unit, rate_bp, cess_bp, default_price_paise)
            VALUES ($name, $hsn, $service, $unit, $rate, $cess, $price)
            """, Args(i));

    public static void Update(SqliteTransaction tx, Item i) =>
        Sql.Execute(tx, """
            UPDATE items SET name = $name, hsn_sac = $hsn, is_service = $service, unit = $unit, rate_bp = $rate, cess_bp = $cess,
                default_price_paise = $price, row_version = row_version + 1
            WHERE id = $id
            """, [.. Args(i), ("$id", i.Id)]);

    static (string, object?)[] Args(Item i) =>
    [
        ("$name", i.Name), ("$hsn", i.HsnSac), ("$service", i.IsService ? 1 : 0), ("$unit", i.Unit),
        ("$rate", i.Rate.BasisPoints), ("$cess", i.CessRate.BasisPoints), ("$price", i.DefaultPrice.Paise),
    ];
}
