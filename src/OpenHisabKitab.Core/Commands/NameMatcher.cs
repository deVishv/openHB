namespace OpenHisabKitab.Core.Commands;

public enum MatchKind { Exact, Prefix, Words, Ambiguous, None }

public sealed record NameMatch(MatchKind Kind, IReadOnlyList<string> Candidates);

/// <summary>Finds a ledger, party, or item name from what the user typed.</summary>
public static class NameMatcher
{
    /// <summary>Tries exact name, then unique prefix, then names containing every typed word. Case and spacing are ignored.</summary>
    public static NameMatch Match(string query, IReadOnlyList<string> names)
    {
        var q = Text.Normalise(query);
        if (q.Length == 0) return new NameMatch(MatchKind.None, []);
        var normal = names.Select(Text.Normalise).ToList();

        var exact = Pick(names, normal, n => n == q);
        if (exact.Count > 0) return Result(MatchKind.Exact, exact);

        var prefix = Pick(names, normal, n => n.StartsWith(q, StringComparison.Ordinal));
        if (prefix.Count > 0) return Result(MatchKind.Prefix, prefix);

        var words = Text.Words(q);
        var contains = Pick(names, normal, n => words.All(w => n.Contains(w, StringComparison.Ordinal)));
        return contains.Count == 0 ? new NameMatch(MatchKind.None, []) : Result(MatchKind.Words, contains);
    }

    static List<string> Pick(IReadOnlyList<string> names, List<string> normal, Func<string, bool> test) =>
        names.Where((_, i) => test(normal[i])).ToList();

    static NameMatch Result(MatchKind single, List<string> found) =>
        new(found.Count == 1 ? single : MatchKind.Ambiguous, found);
}
