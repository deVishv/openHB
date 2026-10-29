namespace OpenHisabKitab.Core.Tests.Amounts;

public class WireAmountTests
{
    [Theory]
    [InlineData("12345.60", 1234560)]
    [InlineData("-12345.60", -1234560)]
    [InlineData("0.05", 5)]
    [InlineData("0.00", 0)]
    [InlineData("92233720368547758.07", long.MaxValue)]
    [InlineData("-92233720368547758.08", long.MinValue)]
    public void Parses_money(string s, long paise)
    {
        Assert.True(WireAmount.TryParseMoney(s, out var m));
        Assert.Equal(paise, m.Paise);
        Assert.Equal(s, WireAmount.Format(m));
    }

    [Theory]
    [InlineData("12345.6")]
    [InlineData("12345")]
    [InlineData("12,345.60")]
    [InlineData("+1.00")]
    [InlineData("01.00")]
    [InlineData("-0.00")]
    [InlineData(" 1.00")]
    [InlineData("1.00 ")]
    [InlineData("1.000")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1e3")]
    [InlineData("-")]
    [InlineData(".50")]
    [InlineData("92233720368547758.08")]
    [InlineData("-92233720368547758.09")]
    [InlineData("١.٠٠")]
    public void Rejects_other_money_forms(string? s) => Assert.False(WireAmount.TryParseMoney(s, out _));

    [Fact]
    public void Quantity_has_three_decimals()
    {
        Assert.True(WireAmount.TryParseQuantity("2.500", out var q));
        Assert.Equal(2500, q.Thousandths);
        Assert.Equal("2.500", WireAmount.Format(q));
        Assert.Equal("-0.001", WireAmount.Format(new Quantity(-1)));
        Assert.False(WireAmount.TryParseQuantity("2.5", out _));
        Assert.False(WireAmount.TryParseQuantity("2.50", out _));
    }

    [Fact]
    public void Rate_has_two_decimals_and_is_never_negative()
    {
        Assert.True(WireAmount.TryParseRate("18.00", out var r));
        Assert.Equal(1800, r.BasisPoints);
        Assert.Equal("0.25", WireAmount.Format(new GstRate(25)));
        Assert.Equal("18.00", WireAmount.Format(r));
        Assert.False(WireAmount.TryParseRate("-5.00", out _));
        Assert.False(WireAmount.TryParseRate("18", out _));
        Assert.False(WireAmount.TryParseRate("21474836.48", out _));
    }
}
