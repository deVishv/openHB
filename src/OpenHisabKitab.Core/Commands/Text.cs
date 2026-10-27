namespace OpenHisabKitab.Core.Commands;

static class Text
{
    /// <summary>
    /// Splits on any whitespace, including tabs and non-breaking spaces. Invisible format characters
    /// such as the zero width space, common in text pasted from web pages, are removed, so a name
    /// matches what the user sees.
    /// </summary>
    public static string[] Words(string? s)
    {
        if (string.IsNullOrEmpty(s)) return [];
        var visible = new string(s.Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());
        return visible.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Trims, collapses whitespace runs to one space, and lower-cases.</summary>
    public static string Normalise(string? s) => string.Join(' ', Words(s)).ToLowerInvariant();
}
