namespace OpenHisabKitab.Core.Tests.Amounts;

public class MoneyTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 1200)]
    [InlineData("12.5", 1250)]
    [InlineData("12.05", 1205)]
    [InlineData("1,23,456.78", 12345678)]
    [InlineData("-35,000", -3500000)]
    [InlineData(" 7.10 ", 710)]
    public void Parse_accepts_valid_input(string text, long paise) =>
        Assert.Equal(new Money(paise), Money.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.234")]
    [InlineData("1..2")]
    [InlineData("--1")]
    [InlineData("1e5")]
    [InlineData(",")]
    [InlineData("1,")]
    [InlineData(",1")]
    [InlineData("92233720368547758.08")]
    public void TryParse_rejects_invalid_input(string text) =>
        Assert.False(Money.TryParse(text, out _));

    [Fact]
    public void Parse_throws_format_exception_on_invalid_input() =>
        Assert.Throws<FormatException>(() => Money.Parse("abc"));

    [Fact]
    public void Addition_and_negation()
    {
        Assert.Equal(new Money(300), new Money(100) + new Money(200));
        Assert.Equal(new Money(-100), -new Money(100));
        Assert.Equal(new Money(50), new Money(150) - new Money(100));
    }

    [Fact]
    public void FromRupees_and_comparison()
    {
        Assert.Equal(new Money(1250000), Money.FromRupees(12500));
        Assert.True(new Money(1) > Money.Zero);
        Assert.True(new Money(-1) < Money.Zero);
    }

    [Fact]
    public void ToString_is_invariant_plain_text()
    {
        Assert.Equal("-1234.50", new Money(-123450).ToString());
        Assert.Equal("0.05", new Money(5).ToString());
    }

    [Fact] // Review Focus 1
    public void Overflow_throws_instead_of_wrapping()
    {
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
        Assert.Throws<OverflowException>(() => -new Money(long.MinValue));
        Assert.Throws<OverflowException>(() => Money.Sum(new[] { new Money(long.MaxValue), new Money(1) }));
        Assert.Throws<OverflowException>(() => Money.FromRupees(long.MaxValue));
    }
}
