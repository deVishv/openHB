using System.Globalization;

namespace OpenHisabKitab.Data;

/// <summary>Dates are stored as yyyy-MM-dd text, so text order is date order.</summary>
internal static class Dates
{
    const string Format_ = "yyyy-MM-dd";

    public static string Format(DateOnly d) => d.ToString(Format_, CultureInfo.InvariantCulture);

    public static DateOnly Parse(string text) => DateOnly.ParseExact(text, Format_, CultureInfo.InvariantCulture);

    public static string? Format(DateOnly? d) => d is { } x ? Format(x) : null;

    public static DateOnly? ParseNullable(string? text) => text is null ? null : Parse(text);
}
