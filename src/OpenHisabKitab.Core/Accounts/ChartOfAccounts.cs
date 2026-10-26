using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Core.Accounts;

public sealed record AccountGroup(GroupId Id, string Name, GroupId? Parent, AccountNature Nature, GroupKind Kind);

public sealed record LedgerAccount(LedgerId Id, string Name, GroupId Group, RequiredLedger? Required);

public sealed class ChartException(string message) : Exception(message);

/// <summary>An immutable tree of groups with ledgers. Adding returns a new chart.</summary>
public sealed class ChartOfAccounts
{
    readonly Dictionary<GroupId, AccountGroup> groupsById;
    readonly Dictionary<LedgerId, LedgerAccount> ledgersById;

    ChartOfAccounts(IReadOnlyList<AccountGroup> groups, IReadOnlyList<LedgerAccount> ledgers)
    {
        Groups = groups;
        Ledgers = ledgers;
        groupsById = groups.ToDictionary(g => g.Id);
        ledgersById = ledgers.ToDictionary(l => l.Id);
    }

    public static ChartOfAccounts Empty { get; } = new([], []);

    public IReadOnlyList<AccountGroup> Groups { get; }

    /// <summary>Ledgers in creation order, which is the order reports use.</summary>
    public IReadOnlyList<LedgerAccount> Ledgers { get; }

    public bool Contains(LedgerId id) => ledgersById.ContainsKey(id);

    public LedgerAccount Ledger(LedgerId id) =>
        ledgersById.TryGetValue(id, out var l) ? l : throw new ChartException($"Unknown ledger {id.Value}.");

    public AccountNature NatureOf(LedgerId id) => groupsById[Ledger(id).Group].Nature;

    /// <summary>True when the ledger's group or any ancestor is Cash in Hand or Bank Accounts.</summary>
    public bool IsCashOrBank(LedgerId id)
    {
        GroupId? g = Ledger(id).Group;
        while (g is { } gid)
        {
            var group = groupsById[gid];
            if (group.Kind is GroupKind.CashInHand or GroupKind.BankAccounts) return true;
            g = group.Parent;
        }
        return false;
    }

    public GroupId Group(GroupKind kind) =>
        Groups.FirstOrDefault(g => g.Kind == kind && kind != GroupKind.None)?.Id
        ?? throw new ChartException($"No group of kind {kind}.");

    public LedgerId Required(RequiredLedger r) =>
        Ledgers.FirstOrDefault(l => l.Required == r)?.Id ?? throw new ChartException($"Required ledger {r} is missing.");

    public (ChartOfAccounts Chart, GroupId Id) AddGroup(string name, GroupId parent) =>
        AddGroup(name, parent, GroupKind.None);

    public (ChartOfAccounts Chart, LedgerId Id) AddLedger(string name, GroupId group) =>
        AddLedger(name, group, null);

    internal (ChartOfAccounts Chart, GroupId Id) AddRootGroup(string name, AccountNature nature)
    {
        var clean = CheckName(name, Groups.Select(g => g.Name), "group");
        var id = new GroupId(Groups.Count + 1);
        return (new ChartOfAccounts([.. Groups, new AccountGroup(id, clean, null, nature, GroupKind.None)], Ledgers), id);
    }

    internal (ChartOfAccounts Chart, GroupId Id) AddGroup(string name, GroupId parent, GroupKind kind)
    {
        var clean = CheckName(name, Groups.Select(g => g.Name), "group");
        var parentGroup = groupsById.TryGetValue(parent, out var p) ? p : throw new ChartException($"Unknown group {parent.Value}.");
        var id = new GroupId(Groups.Count + 1);
        var group = new AccountGroup(id, clean, parent, parentGroup.Nature, kind);
        return (new ChartOfAccounts([.. Groups, group], Ledgers), id);
    }

    internal (ChartOfAccounts Chart, LedgerId Id) AddLedger(string name, GroupId group, RequiredLedger? required)
    {
        var clean = CheckName(name, Ledgers.Select(l => l.Name), "ledger");
        if (!groupsById.ContainsKey(group)) throw new ChartException($"Unknown group {group.Value}.");
        var id = new LedgerId(Ledgers.Count + 1);
        return (new ChartOfAccounts(Groups, [.. Ledgers, new LedgerAccount(id, clean, group, required)]), id);
    }

    // Uniqueness uses the same normalisation as NameMatcher, so two names can never match the same typed text exactly.
    static string CheckName(string name, IEnumerable<string> existing, string what)
    {
        var clean = string.Join(' ', Text.Words(name));
        if (clean.Length == 0) throw new ChartException($"A {what} name cannot be blank.");
        if (clean.Any(char.IsControl)) throw new ChartException($"A {what} name cannot contain control characters.");
        var key = Text.Normalise(clean);
        if (existing.Any(e => Text.Normalise(e) == key))
            throw new ChartException($"A {what} named {clean} already exists.");
        return clean;
    }
}
