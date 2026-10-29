using System.Globalization;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Api;

/// <summary>Reads what a caller sent: query dates and voucher types. Anything not in the exact form is refused, never guessed.</summary>
public static class ApiParse
{
    static ServiceException Invalid(string message) => new(ServiceError.Invalid, message);

    public static DateOnly Date(string? s, string name) =>
        s is null ? throw Invalid($"Add {name}.")
        : DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw Invalid($"{name} must look like 2026-04-01.");

    public static DateRange Range(string? from, string? to)
    {
        var f = Date(from, "from");
        var t = Date(to, "to");
        return f <= t ? new DateRange(f, t) : throw Invalid("from must be on or before to.");
    }

    public static VoucherType? Type(string? s)
    {
        if (s is null) return null;
        foreach (var t in Enum.GetValues<VoucherType>())
            if (Camel(t.ToString()) == s) return t;
        throw Invalid($"type must be one of {string.Join(", ", Enum.GetValues<VoucherType>().Select(t => Camel(t.ToString())))}.");
    }

    /// <summary>The items whose name the search finds with the same matcher the command prompt uses; all of them when there is no search.</summary>
    public static IEnumerable<T> Search<T>(IReadOnlyList<T> items, Func<T, string> name, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return items;
        var found = NameMatcher.Match(search, [.. items.Select(name)]).Candidates.ToHashSet(StringComparer.Ordinal);
        return items.Where(i => found.Contains(name(i)));
    }

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
