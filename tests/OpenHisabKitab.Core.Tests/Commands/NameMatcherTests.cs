namespace OpenHisabKitab.Core.Tests.Commands;

public class NameMatcherTests
{
    static readonly string[] Names = { "HDFC Bank", "HDFC Bank OD", "ICICI Bank", "Cash" };

    [Theory]
    [InlineData("hdfc bank", MatchKind.Exact, "HDFC Bank")]
    [InlineData("  hdfc\u00A0\tbank ", MatchKind.Exact, "HDFC Bank")] // Review Focus 4
    [InlineData("icici", MatchKind.Prefix, "ICICI Bank")]
    [InlineData("bank od", MatchKind.Words, "HDFC Bank OD")]
    public void Single_match(string q, MatchKind kind, string name)
    {
        var m = NameMatcher.Match(q, Names);
        Assert.Equal(kind, m.Kind);
        Assert.Equal(new[] { name }, m.Candidates);
    }

    [Fact]
    public void Names_with_stray_whitespace_still_match() =>
        Assert.Equal(new[] { "HDFC  Bank " }, NameMatcher.Match("hdfc bank", new[] { "HDFC  Bank " }).Candidates);

    [Fact]
    public void Two_exact_matches_are_ambiguous()
    {
        var m = NameMatcher.Match("hdfc bank", new[] { "HDFC Bank", "hdfc  bank" });
        Assert.Equal(MatchKind.Ambiguous, m.Kind);
        Assert.Equal(2, m.Candidates.Count);
    }

    [Fact]
    public void Ambiguous_prefix()
    {
        var m = NameMatcher.Match("hdfc", Names);
        Assert.Equal(MatchKind.Ambiguous, m.Kind);
        Assert.Equal(new[] { "HDFC Bank", "HDFC Bank OD" }, m.Candidates);
    }

    [Fact]
    public void Ambiguous_words()
    {
        var m = NameMatcher.Match("bank", Names);
        Assert.Equal(MatchKind.Ambiguous, m.Kind);
        Assert.Equal(new[] { "HDFC Bank", "HDFC Bank OD", "ICICI Bank" }, m.Candidates);
    }

    [Fact]
    public void None()
    {
        var m = NameMatcher.Match("xyz", Names);
        Assert.Equal(MatchKind.None, m.Kind);
        Assert.Empty(m.Candidates);
    }

    [Fact]
    public void Blank_query_matches_nothing() => Assert.Equal(MatchKind.None, NameMatcher.Match("   ", Names).Kind);
}
