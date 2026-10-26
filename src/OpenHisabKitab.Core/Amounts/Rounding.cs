namespace OpenHisabKitab.Core.Amounts;

/// <summary>The single rounding rule used for all money: half away from zero.</summary>
public static class Rounding
{
    public static long HalfAwayFromZero(decimal value) =>
        checked((long)decimal.Round(value, MidpointRounding.AwayFromZero));
}
