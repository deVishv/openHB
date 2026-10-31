using System.Globalization;

namespace OpenHisabKitab.Host.Application;

/// <summary>One page of a list. Limit 0 means the default; After is the Next cursor of the previous page.</summary>
public sealed record PageRequest(int Limit = 0, string? After = null);

/// <summary>A page of rows. Next is null on the last page.</summary>
public sealed record Page<T>(IReadOnlyList<T> Rows, string? Next);

/// <summary>Page sizes and cursors (spec 13, Remote ready client: lists are fetched in pages). Cursors are opaque to callers.</summary>
internal static class Paging
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 1000;

    public static int Limit(PageRequest page) => page.Limit switch
    {
        0 => DefaultLimit,
        < 0 or > MaxLimit => throw new ServiceException(ServiceError.Invalid, "Ask for at most 1000 rows."),
        var n => n,
    };

    static ServiceException BadCursor() => new(ServiceError.Invalid, "That page is no longer valid. Start again.");

    /// <summary>A cursor for a list in date order: yyyy-MM-dd/id.</summary>
    public static string DateCursor(DateOnly date, long id) =>
        string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd}/{id}");

    public static (DateOnly Date, long Id)? DateCursor(string? after)
    {
        if (string.IsNullOrEmpty(after)) return null;
        var parts = after.Split('/');
        if (parts.Length == 2
            && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return (d, id);
        throw BadCursor();
    }

    public static string IdCursor(long id) => id.ToString(CultureInfo.InvariantCulture);

    public static long? IdCursor(string? after) =>
        string.IsNullOrEmpty(after) ? null
        : long.TryParse(after, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id
        : throw BadCursor();

    /// <summary>Rows fetched with one more than the limit become a page whose Next points after the last row kept.</summary>
    public static Page<T> Cut<T>(IReadOnlyList<T> rows, int limit, Func<T, string> cursor) =>
        rows.Count > limit ? new Page<T>([.. rows.Take(limit)], cursor(rows[limit - 1])) : new Page<T>(rows, null);
}
