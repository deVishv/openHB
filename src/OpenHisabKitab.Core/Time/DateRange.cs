namespace OpenHisabKitab.Core.Time;

/// <summary>An inclusive range of dates.</summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly from, DateOnly to)
    {
        if (from > to) throw new ArgumentException($"Range start {from} is after its end {to}.");
        From = from;
        To = to;
    }

    public DateOnly From { get; }
    public DateOnly To { get; }

    public bool Contains(DateOnly d) => d >= From && d <= To;
}
