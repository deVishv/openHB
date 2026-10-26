using System.Globalization;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Core.Vouchers;

public enum SeriesError { TooLong, InvalidCharacter, BadWidth }

public sealed class SeriesExhaustedException(string message) : Exception(message);

/// <summary>
/// A voucher number series: a prefix pattern, where {FY} becomes the short financial year label,
/// followed by a zero padded running number.
/// </summary>
public sealed record NumberSeries(string Pattern, int Width)
{
    const int GstMaxLength = 16;
    const int MaxWidth = 10;

    public string Prefix(FinancialYear fy) => Pattern.Replace("{FY}", fy.ShortLabel, StringComparison.Ordinal);

    public string Format(FinancialYear fy, long number)
    {
        if (Width is < 1 or > MaxWidth) throw new ArgumentException($"Series width must be 1 to {MaxWidth}.");
        var max = 1L;
        for (var i = 0; i < Width; i++) max *= 10;
        max -= 1;
        if (number < 1 || number > max)
            throw new SeriesExhaustedException($"Number {number} is outside series {Pattern} (1 to {max}).");
        return Prefix(fy) + number.ToString(CultureInfo.InvariantCulture).PadLeft(Width, '0');
    }

    /// <summary>Rule 46(b): at most 16 characters, only letters, digits, hyphen, and slash.</summary>
    public IReadOnlyList<SeriesError> ValidateForGst(FinancialYear fy)
    {
        var errors = new List<SeriesError>();
        var prefix = Prefix(fy);
        if (prefix.Length + Width > GstMaxLength) errors.Add(SeriesError.TooLong);
        if (!prefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '/')) errors.Add(SeriesError.InvalidCharacter);
        if (Width is < 1 or > MaxWidth) errors.Add(SeriesError.BadWidth);
        return errors;
    }
}
