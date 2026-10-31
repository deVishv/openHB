using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Core.Tests.Commands.Keys;

public sealed class KeyChordTests
{
    [Theory]
    [InlineData("Ctrl+Shift+P", "ctrl+shift+p")]
    [InlineData("shift+ctrl+p", "ctrl+shift+p")]
    [InlineData("F5", "f5")]
    [InlineData("alt+Left", "alt+left")]
    [InlineData("ctrl+shift+alt+9", "ctrl+shift+alt+9")]
    [InlineData(" ctrl + tab ", "ctrl+tab")]
    public void Chords_are_canonical(string text, string canonical)
    {
        Assert.True(KeyChord.TryParse(text, out var c));
        Assert.Equal(canonical, c.ToString());
    }

    [Theory]
    [InlineData("ctrl+")]
    [InlineData("ctrl+f13")]
    [InlineData("hyper+a")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ctrl+ctrl+a")]
    [InlineData("a+b")]
    [InlineData("ctrl")]
    public void Bad_chords_are_refused(string? text) => Assert.False(KeyChord.TryParse(text, out _));

    [Fact]
    public void Same_chord_in_another_order_is_equal()
    {
        KeyChord.TryParse("shift+ctrl+tab", out var a);
        KeyChord.TryParse("Ctrl+Shift+Tab", out var b);
        Assert.Equal(a, b);
    }
}
