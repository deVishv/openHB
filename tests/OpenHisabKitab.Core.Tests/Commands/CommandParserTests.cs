namespace OpenHisabKitab.Core.Tests.Commands;

public class CommandParserTests
{
    [Fact]
    public void Parses_code_and_args()
    {
        Assert.True(CommandParser.TryParse("  ledg \t hdfc  Bank ", out var c));
        Assert.Equal("LEDG", c.Code);
        Assert.Equal(new[] { "hdfc", "Bank" }, c.Args);
    }

    [Fact]
    public void Accepts_digit_codes()
    {
        Assert.True(CommandParser.TryParse("gst1 q2", out var c));
        Assert.Equal("GST1", c.Code);
        Assert.Equal(new[] { "q2" }, c.Args);
    }

    [Fact]
    public void Code_without_args_has_empty_args()
    {
        Assert.True(CommandParser.TryParse("TRLB", out var c));
        Assert.Empty(c.Args);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("TRL")]
    [InlineData("TRLB1")]
    [InlineData("12AB")]
    [InlineData("TR-B")]
    public void Rejects(string? input) => Assert.False(CommandParser.TryParse(input, out _));
}
