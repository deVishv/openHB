namespace OpenHisabKitab.Core.Commands;

static class Text
{
    /// <summary>Splits on any whitespace, including tabs and non-breaking spaces.</summary>
    public static string[] Words(string? s) =>
        string.IsNullOrEmpty(s) ? [] : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Trims, collapses whitespace runs to one space, and lower-cases.</summary>
    public static string Normalise(string? s) => string.Join(' ', Words(s)).ToLowerInvariant();
}
