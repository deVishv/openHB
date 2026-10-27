using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>Voucher number series per type and financial year (spec 5.4). Numbers are taken inside the save transaction.</summary>
public static class NumberSeriesRepository
{
    public const string DefaultSeries = "Main";

    static readonly IReadOnlyDictionary<VoucherType, string> DefaultPatterns = new Dictionary<VoucherType, string>
    {
        [VoucherType.Sale] = "INV/{FY}/",
        [VoucherType.Purchase] = "PUR/{FY}/",
        [VoucherType.CreditNote] = "CN/{FY}/",
        [VoucherType.DebitNote] = "DN/{FY}/",
        [VoucherType.Receipt] = "RCT/{FY}/",
        [VoucherType.Payment] = "PAY/{FY}/",
        [VoucherType.Journal] = "JV/{FY}/",
        [VoucherType.Contra] = "CON/{FY}/",
    };

    public static void EnsureDefaults(SqliteTransaction tx, FinancialYear fy)
    {
        foreach (var (type, pattern) in DefaultPatterns)
            Sql.Execute(tx, """
                INSERT INTO number_series(type, series, fy_start, pattern, width, next) VALUES ($type, $series, $fy, $pattern, 5, 1)
                ON CONFLICT DO NOTHING
                """, ("$type", type.ToString()), ("$series", DefaultSeries), ("$fy", fy.StartYear), ("$pattern", pattern));
    }

    /// <summary>Returns the series and the number to use, and moves the counter on in the same statement.</summary>
    public static (NumberSeries Series, long Next) Take(SqliteTransaction tx, VoucherType type, FinancialYear fy, string series = DefaultSeries)
    {
        using var cmd = Sql.Command(tx, """
            UPDATE number_series SET next = next + 1 WHERE type = $type AND series = $series AND fy_start = $fy
            RETURNING pattern, width, next - 1
            """, ("$type", type.ToString()), ("$series", series), ("$fy", fy.StartYear));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException($"No {series} series for {type} in FY {fy.Label}.");
        return (new NumberSeries(r.GetString(0), r.GetInt32(1)), r.GetInt64(2));
    }
}
