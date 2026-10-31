using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Data.CompanyDb;

public sealed record CompanyProfile(string Name, StateCode State, GstRegistration Registration, Gstin? Gstin, string Address, int FirstFyStartYear);

public static class CompanyRepository
{
    /// <summary>Writes a new company file's profile, first financial year with its number series, and chart.</summary>
    public static void Create(SqliteTransaction tx, CompanyProfile p, ChartOfAccounts chart)
    {
        Sql.Execute(tx, """
            INSERT INTO company_profile(id, name, state_code, registration, gstin, address, first_fy_start)
            VALUES (1, $name, $state, $reg, $gstin, $address, $fy)
            """, ProfileArgs(p));
        AddYear(tx, new FinancialYear(p.FirstFyStartYear));
        foreach (var g in chart.Groups) ChartRepository.SaveGroup(tx, g);
        foreach (var l in chart.Ledgers) ChartRepository.SaveLedger(tx, l);
    }

    public static CompanyProfile GetProfile(SqliteConnection c) =>
        Sql.Single(c, "SELECT name, state_code, registration, gstin, address, first_fy_start FROM company_profile WHERE id = 1",
            r => new CompanyProfile(r.GetString(0), new StateCode(r.GetString(1)), Enum.Parse<GstRegistration>(r.GetString(2)),
                StoredGst.Parse(r.NullableString(3)), r.GetString(4), r.GetInt32(5)))
        ?? throw new InvalidDataException("Company profile is missing.");

    /// <summary>Updates everything except the first financial year, which is fixed once books exist.</summary>
    public static void UpdateProfile(SqliteTransaction tx, CompanyProfile p) =>
        Sql.Execute(tx, """
            UPDATE company_profile SET name = $name, state_code = $state, registration = $reg, gstin = $gstin, address = $address
            WHERE id = 1
            """, ProfileArgs(p));

    public static IReadOnlyList<FinancialYear> Years(SqliteConnection c) =>
        Sql.Query(c, "SELECT start_year FROM financial_years ORDER BY start_year", r => new FinancialYear(r.GetInt32(0)));

    /// <summary>Adds a financial year and its default number series, which start at 1.</summary>
    public static void AddYear(SqliteTransaction tx, FinancialYear fy)
    {
        Sql.Execute(tx, "INSERT INTO financial_years(start_year) VALUES ($y)", ("$y", fy.StartYear));
        NumberSeriesRepository.EnsureDefaults(tx, fy);
    }

    public static string? GetSetting(SqliteConnection c, string key) =>
        Sql.Single(c, "SELECT value FROM company_settings WHERE key = $key", r => r.GetString(0), ("$key", key));

    public static void SetSetting(SqliteTransaction tx, string key, string json) =>
        Sql.Execute(tx, "INSERT INTO company_settings(key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", json));

    public static IReadOnlyDictionary<string, string> ListSettings(SqliteConnection c) =>
        Sql.Query(c, "SELECT key, value FROM company_settings ORDER BY key", r => (r.GetString(0), r.GetString(1)))
            .ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal);

    public static bool RemoveSetting(SqliteTransaction tx, string key) =>
        Sql.Execute(tx, "DELETE FROM company_settings WHERE key = $key", ("$key", key)) > 0;

    static (string, object?)[] ProfileArgs(CompanyProfile p) =>
    [
        ("$name", p.Name), ("$state", p.State.Value), ("$reg", p.Registration.ToString()), ("$gstin", p.Gstin?.Value),
        ("$address", p.Address), ("$fy", p.FirstFyStartYear),
    ];
}
