using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Core.Tests.Commands.Keys;

public sealed class WhenClauseTests
{
    static WhenClause Parse(string text)
    {
        Assert.True(WhenClause.TryParse(text, out var w, out var error), error);
        return w;
    }

    static HashSet<string> Ctx(params string[] keys) => [.. keys];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_is_always(string? text)
    {
        Assert.True(WhenClause.TryParse(text, out var w, out _));
        Assert.Same(WhenClause.Always, w);
        Assert.True(w.Matches(Ctx()));
    }

    [Fact]
    public void When_precedence_is_not_and_or()
    {
        var w = Parse("!promptFocus && gridFocus || inputFocus");
        Assert.True(w.Matches(Ctx("gridFocus")));
        Assert.False(w.Matches(Ctx("gridFocus", "promptFocus")));
        Assert.True(w.Matches(Ctx("inputFocus", "promptFocus")));
        Assert.False(w.Matches(Ctx()));
    }

    [Theory]
    [InlineData("promptfocus", "Unknown when key: promptfocus.")]
    [InlineData("gridFocus && editing", "Unknown when key: editing.")]
    [InlineData("gridFocus &&", "Cannot read when: gridFocus &&.")]
    [InlineData("(gridFocus)", "Cannot read when: (gridFocus).")]
    [InlineData("!", "Cannot read when: !.")]
    public void Bad_when_is_an_error(string text, string message)
    {
        Assert.False(WhenClause.TryParse(text, out _, out var error));
        Assert.Equal(message, error);
    }

    [Fact]
    public void Text_is_canonical() =>
        Assert.Equal("!paletteOpen && promptFocus || gridFocus", Parse(" !paletteOpen&&promptFocus ||gridFocus").ToString());
}
