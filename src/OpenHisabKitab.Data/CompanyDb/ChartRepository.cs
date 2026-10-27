using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Data.CompanyDb;

/// <summary>Groups and ledgers, stored with the ids <see cref="ChartOfAccounts"/> assigned.</summary>
public static class ChartRepository
{
    public static ChartOfAccounts Load(SqliteConnection c)
    {
        var groups = Sql.Query(c, "SELECT id, name, parent_id, nature, kind FROM groups ORDER BY id",
            r => new AccountGroup(new GroupId(r.GetInt32(0)), r.GetString(1), r.IsDBNull(2) ? null : new GroupId(r.GetInt32(2)),
                Enum.Parse<AccountNature>(r.GetString(3)), Enum.Parse<GroupKind>(r.GetString(4))));
        var ledgers = Sql.Query(c, "SELECT id, name, group_id, required FROM ledgers ORDER BY id",
            r => new LedgerAccount(new LedgerId(r.GetInt32(0)), r.GetString(1), new GroupId(r.GetInt32(2)),
                r.IsDBNull(3) ? null : Enum.Parse<RequiredLedger>(r.GetString(3))));
        return ChartOfAccounts.Load(groups, ledgers);
    }

    /// <summary>Opening balances that are not zero. Positive is a debit.</summary>
    public static IReadOnlyDictionary<LedgerId, Money> Openings(SqliteConnection c) =>
        Sql.Query(c, "SELECT id, opening_paise FROM ledgers WHERE opening_paise <> 0",
                r => (Id: new LedgerId(r.GetInt32(0)), Amount: new Money(r.GetInt64(1))))
            .ToDictionary(x => x.Id, x => x.Amount);

    public static void SaveGroup(SqliteTransaction tx, AccountGroup g) =>
        Sql.Execute(tx, """
            INSERT INTO groups(id, name, parent_id, nature, kind) VALUES ($id, $name, $parent, $nature, $kind)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, parent_id = excluded.parent_id, nature = excluded.nature, kind = excluded.kind
            """,
            ("$id", g.Id.Value), ("$name", g.Name), ("$parent", g.Parent?.Value), ("$nature", g.Nature.ToString()), ("$kind", g.Kind.ToString()));

    public static void SaveLedger(SqliteTransaction tx, LedgerAccount l) =>
        Sql.Execute(tx, """
            INSERT INTO ledgers(id, name, group_id, required) VALUES ($id, $name, $group, $required)
            ON CONFLICT(id) DO UPDATE SET name = excluded.name, group_id = excluded.group_id, required = excluded.required,
                row_version = row_version + 1
            """,
            ("$id", l.Id.Value), ("$name", l.Name), ("$group", l.Group.Value), ("$required", l.Required?.ToString()));

    public static void DeleteLedger(SqliteTransaction tx, LedgerId id)
    {
        Sql.Execute(tx, "DELETE FROM parties WHERE ledger_id = $id", ("$id", id.Value));
        Sql.Execute(tx, "DELETE FROM ledgers WHERE id = $id", ("$id", id.Value));
    }

    public static void SetOpening(SqliteTransaction tx, LedgerId id, Money amount) =>
        Sql.Execute(tx, "UPDATE ledgers SET opening_paise = $amount, row_version = row_version + 1 WHERE id = $id",
            ("$amount", amount.Paise), ("$id", id.Value));
}
