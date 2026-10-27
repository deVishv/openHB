using Microsoft.Data.Sqlite;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>The period lock (spec 5.6): no voucher dated on or before this date can change.</summary>
public static class LockRepository
{
    public static DateOnly? LockedUpTo(SqliteConnection c) =>
        Sql.Query(c, "SELECT locked_up_to FROM period_lock WHERE id = 1", r => r.NullableString(0))
            .SingleOrDefault() is { } text ? Dates.Parse(text) : null;

    public static void Set(SqliteTransaction tx, DateOnly? upTo) =>
        Sql.Execute(tx, """
            INSERT INTO period_lock(id, locked_up_to) VALUES (1, $d) ON CONFLICT(id) DO UPDATE SET locked_up_to = excluded.locked_up_to
            """, ("$d", upTo is { } d ? Dates.Format(d) : null));
}
