using System.Globalization;
using System.Text;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Formatting;

public enum NegativeStyle { Brackets, Minus }

/// <summary>Formats money with Indian digit grouping, for example 12,34,567.89.</summary>
public static class IndianNumberFormat
{
    public static string Format(Money m, NegativeStyle style = NegativeStyle.Brackets)
    {
        var body = FormatAbsolute(m);
        if (m.Paise >= 0) return body;
        return style == NegativeStyle.Brackets ? $"({body})" : $"-{body}";
    }

    /// <summary>Absolute amount followed by Dr for debit or Cr for credit. Zero has no suffix.</summary>
    public static string FormatDrCr(Money m) =>
        m.Paise switch
        {
            > 0 => $"{FormatAbsolute(m)} Dr",
            < 0 => $"{FormatAbsolute(m)} Cr",
            _ => FormatAbsolute(m),
        };

    static string FormatAbsolute(Money m)
    {
        // ulong holds the magnitude of long.MinValue without overflow.
        var abs = m.Paise < 0 ? (ulong)(-(m.Paise + 1)) + 1 : (ulong)m.Paise;
        var rupees = (abs / 100).ToString(CultureInfo.InvariantCulture);
        var paise = (abs % 100).ToString("D2", CultureInfo.InvariantCulture);
        return $"{Group(rupees)}.{paise}";
    }

    // Last three digits form one group; the digits before them are grouped in pairs.
    static string Group(string digits)
    {
        if (digits.Length <= 3) return digits;
        var head = digits[..^3];
        var sb = new StringBuilder();
        var firstPair = head.Length % 2;
        if (firstPair == 1) sb.Append(head[0]);
        for (var i = firstPair; i < head.Length; i += 2)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(head, i, 2);
        }
        return $"{sb},{digits[^3..]}";
    }
}
