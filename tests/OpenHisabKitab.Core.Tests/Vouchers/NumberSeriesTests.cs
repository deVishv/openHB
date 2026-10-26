namespace OpenHisabKitab.Core.Tests.Vouchers;

public class NumberSeriesTests
{
    static readonly FinancialYear Fy = new(2026);

    [Fact]
    public void Formats() => Assert.Equal("INV/26-27/00001", new NumberSeries("INV/{FY}/", 5).Format(Fy, 1));

    [Fact]
    public void Prefix_expands_fy() => Assert.Equal("INV/26-27/", new NumberSeries("INV/{FY}/", 5).Prefix(Fy));

    [Fact]
    public void Largest_number_fits() => Assert.Equal("INV/26-27/99999", new NumberSeries("INV/{FY}/", 5).Format(Fy, 99999));

    [Fact]
    public void Valid_gst_series() => Assert.Empty(new NumberSeries("INV/{FY}/", 5).ValidateForGst(Fy));

    [Fact]
    public void Exactly_16_is_valid() => Assert.Empty(new NumberSeries("INV/{FY}/", 6).ValidateForGst(Fy));

    [Fact]
    public void Too_long() => Assert.Contains(SeriesError.TooLong, new NumberSeries("INVOICE/{FY}/", 5).ValidateForGst(Fy));

    [Fact]
    public void Space_is_invalid() =>
        Assert.Contains(SeriesError.InvalidCharacter, new NumberSeries("INV {FY}", 4).ValidateForGst(Fy));

    [Fact]
    public void Width_zero_is_invalid() => Assert.Contains(SeriesError.BadWidth, new NumberSeries("S", 0).ValidateForGst(Fy));

    [Fact]
    public void Width_eleven_is_invalid() => Assert.Contains(SeriesError.BadWidth, new NumberSeries("", 11).ValidateForGst(Fy));

    [Theory]
    [InlineData(0)]
    [InlineData(100000)]
    public void Out_of_range_numbers_throw(long n) =>
        Assert.Throws<SeriesExhaustedException>(() => new NumberSeries("INV/{FY}/", 5).Format(Fy, n));
}
