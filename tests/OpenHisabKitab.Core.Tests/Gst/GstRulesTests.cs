namespace OpenHisabKitab.Core.Tests.Gst;

public class GstRulesTests
{
    readonly GstData d = GstData.LoadEmbedded();

    TaxSplit S(string a, string b, bool sez = false) => GstRules.Split(new StateCode(a), new StateCode(b), sez, d);

    [Fact]
    public void Intra_state() => Assert.Equal(TaxSplit.CgstSgst, S("24", "24"));

    [Fact]
    public void Delhi_uses_sgst() => Assert.Equal(TaxSplit.CgstSgst, S("07", "07"));

    [Fact]
    public void Chandigarh_uses_utgst() => Assert.Equal(TaxSplit.CgstUtgst, S("04", "04"));

    [Fact]
    public void Inter_state() => Assert.Equal(TaxSplit.Igst, S("24", "27"));

    [Fact]
    public void Sez_is_inter_state() => Assert.Equal(TaxSplit.Igst, S("24", "24", sez: true));

    [Fact]
    public void Export() => Assert.Equal(TaxSplit.Igst, S("24", "96"));

    [Fact]
    public void Unknown_place_of_supply_throws() => Assert.Throws<KeyNotFoundException>(() => S("24", "25"));
}
