namespace OpenHisabKitab.Core.Commands.Keys;

/// <summary>
/// A VS Code style "when" condition on a keybinding (spec 6.5): context keys joined by "||" and "&amp;&amp;", each optionally
/// negated with "!". "!" binds tightest, then "&amp;&amp;", then "||". There are no parentheses.
/// </summary>
public sealed class WhenClause
{
    public static IReadOnlySet<string> ContextKeys { get; } = new HashSet<string> { "promptFocus", "inputFocus", "gridFocus", "paletteOpen" };

    public static WhenClause Always { get; } = new([]);

    // Any of these groups matching makes the clause match; a group matches when every term does.
    readonly IReadOnlyList<IReadOnlyList<(string Key, bool Negated)>> anyOf;

    WhenClause(IReadOnlyList<IReadOnlyList<(string Key, bool Negated)>> anyOf) => this.anyOf = anyOf;

    /// <summary>Null or blank text is <see cref="Always"/>.</summary>
    public static bool TryParse(string? text, out WhenClause clause, out string? error)
    {
        clause = Always;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var groups = new List<IReadOnlyList<(string, bool)>>();
        foreach (var group in text.Split("||"))
        {
            var terms = new List<(string, bool)>();
            foreach (var raw in group.Split("&&"))
            {
                var term = raw.Trim();
                var negated = term.StartsWith('!');
                if (negated) term = term[1..].Trim();
                if (term.Length == 0 || !term.All(c => char.IsAsciiLetterOrDigit(c) || c == '.'))
                {
                    error = $"Cannot read when: {text.Trim()}.";
                    return false;
                }
                if (!ContextKeys.Contains(term))
                {
                    error = $"Unknown when key: {term}.";
                    return false;
                }
                terms.Add((term, negated));
            }
            groups.Add(terms);
        }
        clause = new WhenClause(groups);
        return true;
    }

    public bool Matches(IReadOnlySet<string> context) =>
        anyOf.Count == 0 || anyOf.Any(g => g.All(t => context.Contains(t.Key) != t.Negated));

    public override string ToString() =>
        string.Join(" || ", anyOf.Select(g => string.Join(" && ", g.Select(t => (t.Negated ? "!" : "") + t.Key))));
}
