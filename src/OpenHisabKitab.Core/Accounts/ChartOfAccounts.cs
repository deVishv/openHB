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

    /// <summary>Rebuilds a stored chart, checking every rule the add methods enforce one at a time.</summary>
    public static ChartOfAccounts Load(IEnumerable<AccountGroup> groups, IEnumerable<LedgerAccount> ledgers)
    {
        var groupList = groups.ToList();
        var ledgerList = ledgers.ToList();

        if (groupList.Select(g => g.Id).Distinct().Count() != groupList.Count) throw new ChartException("Duplicate group id.");
        if (ledgerList.Select(l => l.Id).Distinct().Count() != ledgerList.Count) throw new ChartException("Duplicate ledger id.");
        CheckUniqueNames(groupList.Select(g => g.Name), "group");
        CheckUniqueNames(ledgerList.Select(l => l.Name), "ledger");

        var byId = groupList.ToDictionary(g => g.Id);
        foreach (var g in groupList)
        {
            if (g.Parent is not { } parentId) continue;
            if (!byId.TryGetValue(parentId, out var parent)) throw new ChartException($"Group {g.Name} has an unknown parent.");
            if (parent.Nature != g.Nature) throw new ChartException($"Group {g.Name} differs in nature from its parent.");
            var seen = new HashSet<GroupId> { g.Id };
            for (GroupId? up = parentId; up is { } u; up = byId[u].Parent)
                if (!seen.Add(u)) throw new ChartException($"Group {g.Name} is inside itself.");
        }

        var kinds = groupList.Where(g => g.Kind != GroupKind.None).GroupBy(g => g.Kind).FirstOrDefault(k => k.Count() > 1);
        if (kinds is not null) throw new ChartException($"More than one group of kind {kinds.Key}.");

        var lost = ledgerList.FirstOrDefault(l => !byId.ContainsKey(l.Group));
        if (lost is not null) throw new ChartException($"Ledger {lost.Name} is in an unknown group.");

        foreach (var r in Enum.GetValues<RequiredLedger>())
        {
            var count = ledgerList.Count(l => l.Required == r);
            if (count != 1) throw new ChartException($"Required ledger {r} appears {count} times.");
        }

        return new ChartOfAccounts(groupList, ledgerList);
    }

    public IReadOnlyList<AccountGroup> Groups { get; }

    /// <summary>Ledgers in creation order, which is the order reports use.</summary>
    public IReadOnlyList<LedgerAccount> Ledgers { get; }

    public bool Contains(LedgerId id) => ledgersById.ContainsKey(id);

    public LedgerAccount Ledger(LedgerId id) =>
        ledgersById.TryGetValue(id, out var l) ? l : throw new ChartException($"Unknown ledger {id.Value}.");

    public AccountNature NatureOf(LedgerId id) => groupsById[Ledger(id).Group].Nature;

    /// <summary>True when the ledger's group or any ancestor is Cash in Hand, Bank Accounts, or Bank Overdraft.</summary>
    public bool IsCashOrBank(LedgerId id)
    {
        GroupId? g = Ledger(id).Group;
        while (g is { } gid)
        {
            var group = groupsById[gid];
            if (group.Kind is GroupKind.CashInHand or GroupKind.BankAccounts or GroupKind.BankOverdraft) return true;
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

    public ChartOfAccounts RenameGroup(GroupId id, string name)
    {
        var group = groupsById.TryGetValue(id, out var g) ? g : throw new ChartException($"Unknown group {id.Value}.");
        var clean = CheckName(name, Groups.Where(x => x.Id != id).Select(x => x.Name), "group");
        return new ChartOfAccounts([.. Groups.Select(x => x.Id == id ? group with { Name = clean } : x)], Ledgers);
    }

    public ChartOfAccounts RenameLedger(LedgerId id, string name)
    {
        var ledger = Ledger(id);
        var clean = CheckName(name, Ledgers.Where(x => x.Id != id).Select(x => x.Name), "ledger");
        return new ChartOfAccounts(Groups, [.. Ledgers.Select(x => x.Id == id ? ledger with { Name = clean } : x)]);
    }

    /// <summary>Removes a ledger. The caller must first check that nothing posts to it.</summary>
    public ChartOfAccounts RemoveLedger(LedgerId id)
    {
        var ledger = Ledger(id);
        if (ledger.Required is not null) throw new ChartException($"Required ledger {ledger.Name} cannot be deleted.");
        return new ChartOfAccounts(Groups, [.. Ledgers.Where(x => x.Id != id)]);
    }

    internal (ChartOfAccounts Chart, GroupId Id) AddRootGroup(string name, AccountNature nature)
    {
        var clean = CheckName(name, Groups.Select(g => g.Name), "group");
        var id = NextGroupId();
        return (new ChartOfAccounts([.. Groups, new AccountGroup(id, clean, null, nature, GroupKind.None)], Ledgers), id);
    }

    internal (ChartOfAccounts Chart, GroupId Id) AddGroup(string name, GroupId parent, GroupKind kind)
    {
        var clean = CheckName(name, Groups.Select(g => g.Name), "group");
        var parentGroup = groupsById.TryGetValue(parent, out var p) ? p : throw new ChartException($"Unknown group {parent.Value}.");
        var id = NextGroupId();
        var group = new AccountGroup(id, clean, parent, parentGroup.Nature, kind);
        return (new ChartOfAccounts([.. Groups, group], Ledgers), id);
    }

    internal (ChartOfAccounts Chart, LedgerId Id) AddLedger(string name, GroupId group, RequiredLedger? required)
    {
        var clean = CheckName(name, Ledgers.Select(l => l.Name), "ledger");
        if (!groupsById.ContainsKey(group)) throw new ChartException($"Unknown group {group.Value}.");
        var id = NextLedgerId();
        return (new ChartOfAccounts(Groups, [.. Ledgers, new LedgerAccount(id, clean, group, required)]), id);
    }

    // Ids are never derived from counts, so a removal cannot make a new id collide with a remaining one.
    GroupId NextGroupId() => new(Groups.Count == 0 ? 1 : Groups.Max(g => g.Id.Value) + 1);

    LedgerId NextLedgerId() => new(Ledgers.Count == 0 ? 1 : Ledgers.Max(l => l.Id.Value) + 1);

    static void CheckUniqueNames(IEnumerable<string> names, string what)
    {
        var dup = names.GroupBy(Text.Normalise).FirstOrDefault(n => n.Count() > 1);
        if (dup is not null) throw new ChartException($"More than one {what} named {dup.First()}.");
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
