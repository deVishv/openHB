using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Lookups;

/// <summary>
/// A field that names a ledger, party, or item (spec 6.2, Names). Typing lists matches; Enter or Tab commits: exact name,
/// then unique prefix, then words, ignoring case. Several matches keep the pick list open; none offers to create one.
/// </summary>
public sealed partial class LookupField<T>(Func<IReadOnlyList<T>> source, Func<T, string> name) : ObservableObject where T : class
{
    const int MaxSuggestions = 20;
    bool setting;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<T> Suggestions { get; private set; } = [];

    [ObservableProperty]
    public partial T? Value { get; private set; }

    /// <summary>True after Commit found no match, until the text changes.</summary>
    [ObservableProperty]
    public partial bool OffersCreate { get; private set; }

    /// <summary>One match sets Value and the text to its name. Several keep Suggestions and return false. None sets OffersCreate. Empty text clears Value.</summary>
    public bool Commit()
    {
        if (Text.Trim().Length == 0)
        {
            Set(null);
            return true;
        }
        if (Value is { } v && name(v) == Text) return true;
        var items = source();
        var match = NameMatcher.Match(Text, [.. items.Select(name)]);
        switch (match.Kind)
        {
            case MatchKind.None:
                Suggestions = [];
                OffersCreate = true;
                return false;
            case MatchKind.Ambiguous:
                Suggestions = Pick(items, match);
                return false;
            default:
                Set(items.First(i => name(i) == match.Candidates[0]));
                return true;
        }
    }

    public void Pick(T item) => Set(item);

    public void Set(T? item)
    {
        setting = true;
        try
        {
            Value = item;
            Text = item is null ? "" : name(item);
            Suggestions = [];
            OffersCreate = false;
        }
        finally { setting = false; }
    }

    partial void OnTextChanged(string value)
    {
        if (setting) return;
        OffersCreate = false;
        if (Value is { } v && name(v) != value) Value = null;
        var items = source();
        var match = NameMatcher.Match(value, [.. items.Select(name)]);
        Suggestions = match.Kind == MatchKind.None ? [] : Pick(items, match);
    }

    IReadOnlyList<T> Pick(IReadOnlyList<T> items, NameMatch match)
    {
        var wanted = match.Candidates.ToHashSet(StringComparer.Ordinal);
        return [.. items.Where(i => wanted.Contains(name(i))).Take(MaxSuggestions)];
    }
}
