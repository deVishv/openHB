using System.Globalization;

namespace OpenHisabKitab.Core.Amounts;

/// <summary>An amount of money in whole paise. Arithmetic is checked and throws on overflow.</summary>
public readonly record struct Money(long Paise) : IComparable<Money>
{
    public static Money Zero => default;

    public static Money FromRupees(long rupees) => new(checked(rupees * 100));

    public static Money operator +(Money a, Money b) => new(checked(a.Paise + b.Paise));
    public static Money operator -(Money a, Money b) => new(checked(a.Paise - b.Paise));
    public static Money operator -(Money a) => new(checked(-a.Paise));
    public static bool operator <(Money a, Money b) => a.Paise < b.Paise;
    public static bool operator >(Money a, Money b) => a.Paise > b.Paise;
    public static bool operator <=(Money a, Money b) => a.Paise <= b.Paise;
    public static bool operator >=(Money a, Money b) => a.Paise >= b.Paise;

    public int CompareTo(Money other) => Paise.CompareTo(other.Paise);

    public static Money Sum(IEnumerable<Money> values)
    {
        var total = Zero;
        foreach (var v in values) total += v;
        return total;
    }

    public static Money Parse(string text) =>
        TryParse(text, out var money) ? money : throw new FormatException($"Invalid amount: {text}");

    /// <summary>
    /// Accepts an optional leading minus, digits with commas only between digits,
    /// and an optional decimal point followed by one or two digits.
    /// </summary>
    public static bool TryParse(string? text, out Money money)
    {
        money = Zero;
        if (text is null) return false;
        var s = text.Trim();
        var negative = s.StartsWith('-');
        if (negative) s = s[1..];
        if (s.Length == 0) return false;

        var point = s.IndexOf('.');
        var whole = point < 0 ? s : s[..point];
        var fraction = point < 0 ? "" : s[(point + 1)..];
        if (point >= 0 && (fraction.Length is < 1 or > 2 || !fraction.All(char.IsAsciiDigit))) return false;
        if (whole.Length == 0) return false;

        if (!ValidGrouping(whole)) return false;
        var digits = whole.Replace(",", "", StringComparison.Ordinal);

        var paiseText = digits + fraction.PadRight(2, '0');
        if (!decimal.TryParse(paiseText, NumberStyles.None, CultureInfo.InvariantCulture, out var paise)) return false;
        if (negative) paise = -paise;
        if (paise < long.MinValue || paise > long.MaxValue) return false;
        money = new Money((long)paise);
        return true;
    }

    /// <summary>
    /// Digits with no commas, or commas in Indian grouping (12,34,567) or Western grouping (1,234,567).
    /// Anything else, such as 12,50 typed with a decimal comma, is rejected rather than read as a larger amount.
    /// </summary>
    static bool ValidGrouping(string whole)
    {
        var groups = whole.Split(',');
        if (groups.Any(g => g.Length == 0 || !g.All(char.IsAsciiDigit))) return false;
        if (groups.Length == 1) return true;
        if (groups[^1].Length != 3) return false;
        var middle = groups[1..^1];
        var indian = groups[0].Length <= 2 && middle.All(g => g.Length == 2);
        var western = groups[0].Length <= 3 && middle.All(g => g.Length == 3);
        return indian || western;
    }

    public override string ToString()
    {
        var abs = Paise < 0 ? (ulong)(-(Paise + 1)) + 1 : (ulong)Paise;
        var sign = Paise < 0 ? "-" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs / 100}.{abs % 100:D2}");
    }
}
