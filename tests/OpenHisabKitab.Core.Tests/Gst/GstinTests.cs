namespace OpenHisabKitab.Core.Tests.Gst;

public class GstinTests
{
    static readonly GstData Data = GstData.LoadEmbedded();

    [Theory]
    [InlineData("27AAPFU0939F1ZV", "27")]
    [InlineData("29AAGCB7383J1Z4", "29")]
    [InlineData(" 24aaacc1206d1zm ", "24")]
    public void Valid_gstin(string text, string state)
    {
        Assert.True(Gstin.TryParse(text, Data, out var g));
        Assert.Equal(state, g.State.Value);
        Assert.Equal(text.Trim().ToUpperInvariant(), g.Value);
    }

    [Theory]
    [InlineData("07AAACI1681G1ZN")]   // checksum should be R
    [InlineData("27AAPFU0939F1Z")]    // 14 characters
    [InlineData("00AAPFU0939F1ZB")]   // checksum valid, but no state 00
    [InlineData("27AAPFU0939F1Z-")]
    [InlineData("27AAPFU0939F1ZV1")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_gstin(string? text) => Assert.False(Gstin.TryParse(text, Data, out _));

    [Fact]
    public void Lower_case_is_upper_cased_with_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.True(Gstin.TryParse("29aagcb7383j1z4", Data, out var g));
            Assert.Equal("29AAGCB7383J1Z4", g.Value);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
