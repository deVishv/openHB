using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Core.Formatting;

/// <summary>
/// An amount written out in the Indian scale, as printed invoices and vouchers show it:
/// "Rupees Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven and Eighty Nine Paise Only".
/// </summary>
public static class AmountInWords
{
    static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen",
        "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];

    static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Rupees(Money m)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(m.Paise);
        var rupees = m.Paise / 100;
        var paise = m.Paise % 100;
        var words = "Rupees " + Whole(rupees);
        if (paise != 0) words += " and " + Whole(paise) + " Paise";
        return words + " Only";
    }

    /// <summary>A whole number in words: crore takes whatever is above it, so 1,000 crore is "One Thousand Crore".</summary>
    static string Whole(long n)
    {
        if (n == 0) return Ones[0];
        var parts = new List<string>();
        var crore = n / 1_00_00_000;
        if (crore > 0) parts.Add(Whole(crore) + " Crore");
        n %= 1_00_00_000;
        AddGroup(parts, n / 1_00_000, "Lakh");
        n %= 1_00_000;
        AddGroup(parts, n / 1000, "Thousand");
        n %= 1000;
        AddGroup(parts, n / 100, "Hundred");
        n %= 100;
        if (n > 0) parts.Add(BelowHundred(n));
        return string.Join(' ', parts);
    }

    static void AddGroup(List<string> parts, long count, string name)
    {
        if (count > 0) parts.Add(BelowHundred(count) + " " + name);
    }

    static string BelowHundred(long n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 == 0 ? "" : " " + Ones[n % 10]);
}
