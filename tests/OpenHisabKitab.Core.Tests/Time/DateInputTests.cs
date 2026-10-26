namespace OpenHisabKitab.Core.Tests.Time;

public class DateInputTests
{
    static readonly DateOnly Today = new(2026, 10, 26);

    [Theory]
    [InlineData("31-03-2027", 2027, 3, 31)]
    [InlineData("1/4/2026", 2026, 4, 1)]
    [InlineData("31-3-27", 2027, 3, 31)]
    [InlineData("5-1", 2027, 1, 5)]
    [InlineData("5-4", 2026, 4, 5)]
    [InlineData("TODAY", 2026, 10, 26)]
    [InlineData("29-2-28", 2028, 2, 29)]
    public void Accepts(string token, int y, int m, int d)
    {
        Assert.True(DateInput.TryParse(token, Today, out var date));
        Assert.Equal(new DateOnly(y, m, d), date);
    }

    [Theory] // Review Focus 3
    [InlineData("29-2-27")]
    [InlineData("31-4-2026")]
    [InlineData("0-1-2026")]
    [InlineData("1-13")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1-1-1-1")]
    public void Rejects(string token) => Assert.False(DateInput.TryParse(token, Today, out _));
}
