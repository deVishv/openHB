namespace OpenHisabKitab.Core.Tests.Formatting;

public class IndianNumberFormatTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(99999, "999.99")]
    [InlineData(100000, "1,000.00")]
    [InlineData(12345678, "1,23,456.78")]
    [InlineData(123456789, "12,34,567.89")]
    [InlineData(10000000000, "10,00,00,000.00")]
    [InlineData(-3500000, "(35,000.00)")]
    public void Format_uses_indian_grouping(long paise, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.Format(new Money(paise)));

    [Fact]
    public void Format_minus_style() =>
        Assert.Equal("-35,000.00", IndianNumberFormat.Format(new Money(-3500000), NegativeStyle.Minus));

    [Fact]
    public void Format_handles_long_min_value() =>
        Assert.Equal("(92,23,37,20,36,85,47,758.08)", IndianNumberFormat.Format(new Money(long.MinValue)));

    [Theory]
    [InlineData(24850000, "2,48,500.00 Dr")]
    [InlineData(-3500000, "35,000.00 Cr")]
    [InlineData(0, "0.00")]
    public void FormatDrCr(long paise, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.FormatDrCr(new Money(paise)));

    [Fact] // Review Focus 2
    public void Results_ignore_current_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("12,34,567.89", IndianNumberFormat.Format(new Money(123456789)));
            Assert.Equal(new Money(1250), Money.Parse("12.50"));
            Assert.Equal("12.50", new Money(1250).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
