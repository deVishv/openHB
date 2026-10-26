namespace OpenHisabKitab.Core.Gst;

/// <summary>A GST rate in hundredths of a percent, so 18% is 1800.</summary>
public readonly record struct GstRate
{
    public GstRate(int basisPoints)
    {
        if (basisPoints < 0) throw new ArgumentException("A GST rate cannot be negative.", nameof(basisPoints));
        BasisPoints = basisPoints;
    }

    public int BasisPoints { get; }
}
