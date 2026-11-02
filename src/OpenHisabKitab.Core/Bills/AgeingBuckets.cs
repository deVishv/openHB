using System.Globalization;

namespace OpenHisabKitab.Core.Bills;

/// <summary>
/// The ageing columns of OUTS as rising day limits, such as 30, 60, 90: bucket 0 is not due, bucket i holds bills up to
/// Days[i - 1] days overdue, and the last bucket holds everything beyond the last limit.
/// </summary>
public sealed record AgeingBuckets(IReadOnlyList<int> Days)
{
    const int MaxColumns = 5;
    const int MaxDays = 999;

    public static AgeingBuckets Default { get; } = new([30, 60, 90]);

    /// <summary>Not due, one per limit, and beyond the last.</summary>
    public int Count => Days.Count + 2;

    /// <summary>1 to 5 rising whole numbers from 1 to 999, separated by commas.</summary>
    public static bool TryParse(string text, out AgeingBuckets buckets)
    {
        buckets = Default;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is 0 or > MaxColumns) return false;
        var days = new List<int>();
        foreach (var p in parts)
        {
            if (p.Length == 0 || !p.All(char.IsAsciiDigit)
                || !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var d) || d is < 1 or > MaxDays) return false;
            if (days.Count > 0 && d <= days[^1]) return false;
            days.Add(d);
        }
        buckets = new AgeingBuckets(days);
        return true;
    }

    public int BucketOf(int daysOverdue)
    {
        if (daysOverdue <= 0) return 0;
        for (var i = 0; i < Days.Count; i++)
            if (daysOverdue <= Days[i]) return i + 1;
        return Days.Count + 1;
    }

    public bool Equals(AgeingBuckets? other) => other is not null && Days.SequenceEqual(other.Days);

    public override int GetHashCode() => Days.Aggregate(17, (h, d) => h * 31 + d);
}
