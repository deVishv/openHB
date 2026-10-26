namespace OpenHisabKitab.Core.Tests.Time;

public class PeriodInputTests
{
    static readonly DateOnly Today = new(2026, 10, 26);

    static DateRange R(int y1, int m1, int d1, int y2, int m2, int d2) => new(new(y1, m1, d1), new(y2, m2, d2));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Accepts(string[] tokens, DateRange expected)
    {
        Assert.True(PeriodInput.TryParse(tokens, Today, out var range));
        Assert.Equal(expected, range);
    }

    public static TheoryData<string[], DateRange> Cases => new()
    {
        { new[] { "apr" }, R(2026, 4, 1, 2026, 4, 30) },
        { new[] { "January" }, R(2027, 1, 1, 2027, 1, 31) },
        { new[] { "q1" }, R(2026, 4, 1, 2026, 6, 30) },
        { new[] { "Q4" }, R(2027, 1, 1, 2027, 3, 31) },
        { new[] { "fy" }, R(2026, 4, 1, 2027, 3, 31) },
        { new[] { "fy25" }, R(2025, 4, 1, 2026, 3, 31) },
        { new[] { "1-4", "30-6" }, R(2026, 4, 1, 2026, 6, 30) },
    };

    [Theory]
    [InlineData("30-6", "1-4")]
    [InlineData("q5", null)]
    [InlineData("fyx", null)]
    [InlineData("xyz", null)]
    public void Rejects(string a, string? b) =>
        Assert.False(PeriodInput.TryParse(b is null ? new[] { a } : new[] { a, b }, Today, out _));

    [Fact]
    public void Rejects_empty_and_too_many_tokens()
    {
        Assert.False(PeriodInput.TryParse(Array.Empty<string>(), Today, out _));
        Assert.False(PeriodInput.TryParse(new[] { "1-4", "30-6", "1-7" }, Today, out _));
    }
}
