using System.Globalization;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Core.Amounts;

/// <summary>
/// The REST and MCP wire form (spec 4.1): money as "12345.60", quantities as "2.500", rates as "18.00".
/// Exactly that many decimals, an optional leading minus, no leading zeros, no commas, no spaces. Anything else is refused.
/// </summary>
public static class WireAmount
{
    public static string Format(Money m) => Format(m.Paise, 2);

    public static string Format(Quantity q) => Format(q.Thousandths, 3);

    public static string Format(GstRate r) => Format(r.BasisPoints, 2);

    public static bool TryParseMoney(string? s, out Money m)
    {
        var ok = TryParse(s, 2, out var v);
        m = new Money(v);
        return ok;
    }

    public static bool TryParseQuantity(string? s, out Quantity q)
    {
        var ok = TryParse(s, 3, out var v);
        q = new Quantity(v);
        return ok;
    }

    public static bool TryParseRate(string? s, out GstRate r)
    {
        r = default;
        if (s is null || s.StartsWith('-') || !TryParse(s, 2, out var v) || v > int.MaxValue) return false;
        r = new GstRate((int)v);
        return true;
    }

    static string Format(long value, int decimals)
    {
        // ulong holds the magnitude of long.MinValue without overflow.
        var abs = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        var scale = decimals == 3 ? 1000UL : 100UL;
        var fraction = (abs % scale).ToString("D" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"{(value < 0 ? "-" : "")}{abs / scale}.{fraction}");
    }

    static bool TryParse(string? s, int decimals, out long value)
    {
        value = 0;
        if (s is null) return false;
        var negative = s.StartsWith('-');
        var body = negative ? s[1..] : s;
        var point = body.IndexOf('.');
        if (point < 1 || body.Length - point - 1 != decimals) return false;
        var whole = body[..point];
        if (whole.Length > 1 && whole[0] == '0') return false;
        var digits = string.Concat(whole, body.AsSpan(point + 1));
        if (!digits.All(char.IsAsciiDigit)) return false;

        // Accumulated as a negative number so that long.MinValue parses.
        long acc = 0;
        try
        {
            foreach (var c in digits) acc = checked(acc * 10 - (c - '0'));
            if (negative)
            {
                if (acc == 0) return false;
                value = acc;
            }
            else
            {
                value = checked(-acc);
            }
            return true;
        }
        catch (OverflowException)
        {
            value = 0;
            return false;
        }
    }
}
