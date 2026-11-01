using OpenHisabKitab.Client.Model.Lookups;

namespace OpenHisabKitab.Client.Model.Tests.Lookups;

public sealed class LookupFieldTests
{
    sealed record Named(string Name);

    static LookupField<Named> Field(params string[] names)
    {
        var items = names.Select(n => new Named(n)).ToList();
        return new LookupField<Named>(() => items, x => x.Name);
    }

    [Fact]
    public void Exact_name_wins_over_prefix()
    {
        var f = Field("HDFC Bank", "HDFC Bank OD");
        f.Text = "hdfc bank";
        Assert.True(f.Commit());
        Assert.Equal("HDFC Bank", f.Value!.Name);
        Assert.Equal("HDFC Bank", f.Text);
    }

    [Fact]
    public void Case_and_spacing_do_not_matter()
    {
        var f = Field("HDFC Bank", "Cash");
        f.Text = "  hdfc   bank ";
        Assert.True(f.Commit());
        Assert.Equal("HDFC Bank", f.Value!.Name);
    }

    [Fact]
    public void A_unique_prefix_is_enough()
    {
        var f = Field("HDFC Bank", "Cash");
        f.Text = "hd";
        Assert.True(f.Commit());
        Assert.Equal("HDFC Bank", f.Value!.Name);
    }

    [Fact]
    public void Several_matches_keep_the_pick_list()
    {
        var f = Field("HDFC Bank", "HDFC Bank OD", "Cash");
        f.Text = "hdfc";
        Assert.False(f.Commit());
        Assert.Null(f.Value);
        Assert.Equal(["HDFC Bank", "HDFC Bank OD"], f.Suggestions.Select(s => s.Name));
        Assert.False(f.OffersCreate);
        f.Pick(f.Suggestions[1]);
        Assert.Equal(("HDFC Bank OD", "HDFC Bank OD"), (f.Value!.Name, f.Text));
        Assert.Empty(f.Suggestions);
    }

    [Fact]
    public void No_match_offers_create()
    {
        var f = Field("HDFC Bank");
        f.Text = "Ravi Stores";
        Assert.False(f.Commit());
        Assert.True(f.OffersCreate);
        Assert.Null(f.Value);
        f.Text = "hd";
        Assert.False(f.OffersCreate);
    }

    [Fact]
    public void Empty_text_clears_the_value()
    {
        var f = Field("HDFC Bank");
        f.Set(new Named("HDFC Bank"));
        f.Text = "  ";
        Assert.True(f.Commit());
        Assert.Null(f.Value);
    }

    [Fact]
    public void Typing_lists_suggestions_at_most_twenty()
    {
        var f = Field([.. Enumerable.Range(1, 30).Select(i => $"Party {i:D2}")]);
        f.Text = "party";
        Assert.Equal(20, f.Suggestions.Count);
    }

    [Fact]
    public void Editing_the_text_after_a_pick_clears_the_value()
    {
        var f = Field("HDFC Bank", "Cash");
        f.Set(new Named("Cash"));
        Assert.Equal("Cash", f.Text);
        f.Text = "Cas";
        Assert.Null(f.Value);
    }
}
