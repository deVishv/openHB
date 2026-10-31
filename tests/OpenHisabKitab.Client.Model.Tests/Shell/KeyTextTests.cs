using OpenHisabKitab.Client.Model.Shell;
using static OpenHisabKitab.Client.Model.Tests.Shell.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Shell;

public sealed class KeyTextTests
{
    [Theory]
    [InlineData("ctrl+shift+p", "Ctrl+Shift+P")]
    [InlineData("f5", "F5")]
    [InlineData("alt+left", "Alt+Left")]
    [InlineData("ctrl+pagedown", "Ctrl+PageDown")]
    [InlineData("shift+enter", "Shift+Enter")]
    public void Keys_show_as_menus_show_them(string chord, string text) => Assert.Equal(text, KeyText.Display(Chord(chord)));

    [Fact]
    public void F_key_text_names_the_code_when_there_is_one()
    {
        Assert.Equal("F6 SALE", new FKeyLabel("F6", "SALE").Text);
        Assert.Equal("F6", new FKeyLabel("F6", null).Text);
    }
}
