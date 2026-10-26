namespace OpenHisabKitab.Core.Tests.Amounts;

public class RoundingTests
{
    [Theory]
    [InlineData("2.5", 3)]
    [InlineData("-2.5", -3)]
    [InlineData("2.4999", 2)]
    [InlineData("0.5", 1)]
    [InlineData("-0.5", -1)]
    [InlineData("7", 7)]
    public void HalfAwayFromZero(string value, long expected) =>
        Assert.Equal(expected, Rounding.HalfAwayFromZero(decimal.Parse(value, CultureInfo.InvariantCulture)));
}
