using OpenHisabKitab.Client.Model.Printing;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

public sealed class TextWrapTests
{
    static readonly PrintFont Body = new(PrintFace.Text, 10);
    static readonly ITextMeasure M = PdfMeasure.Instance;

    [Fact]
    public void Wrap_breaks_at_spaces_splits_long_words_and_keeps_newlines()
    {
        var width = M.Width("Plot 9, MIDC", Body) + 0.5;
        Assert.Equal(["Plot 9, MIDC", "Chakan"], TextWrap.Lines("Plot 9, MIDC Chakan", width, Body, M));
        Assert.Equal(["HDFC Bank", "A/c 123"], TextWrap.Lines("HDFC Bank\nA/c 123", 500, Body, M));
        Assert.Equal(["One", "", "Two"], TextWrap.Lines("One\n\nTwo", 500, Body, M));
        var narrow = M.Width("ABCDE", Body) + 0.5;
        var parts = TextWrap.Lines("ABCDEFGHIJKL", narrow, Body, M);
        Assert.Equal("ABCDEFGHIJKL", string.Concat(parts));
        Assert.Equal("ABCDE", parts[0]);
        Assert.Equal([""], TextWrap.Lines("", 100, Body, M));
    }

    [Fact]
    public void Wrapped_lines_never_exceed_the_width()
    {
        var random = new Random(46);
        const string letters = "abc defgh ijk lmnopq rs tuvw xyz ABC ₹ 1,23,456.00 WWWWW";
        for (var i = 0; i < 200; i++)
        {
            var text = new string([.. Enumerable.Range(0, random.Next(1, 120)).Select(_ => letters[random.Next(letters.Length)])]);
            var width = random.Next(20, 301);
            var lines = TextWrap.Lines(text, width, Body, M);
            Assert.All(lines, l => Assert.True(M.Width(l, Body) <= width, $"'{l}' wider than {width}"));
            Assert.Equal(text.Replace(" ", "", StringComparison.Ordinal), string.Concat(lines).Replace(" ", "", StringComparison.Ordinal));
        }
    }
}
