using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Core.Tests.Gst;

public sealed class TaxModeTests
{
    [Theory]
    [InlineData("Regular", "Regular", "Outward", false, true, TaxDecision.Taxed)]
    [InlineData("Regular", "Regular", "Inward", true, true, TaxDecision.Taxed)]
    [InlineData("Regular", "ExportUnderLut", "Outward", false, true, TaxDecision.TaxFree)]
    [InlineData("Regular", "SezWithoutTax", "Outward", false, true, TaxDecision.TaxFree)]
    [InlineData("Regular", "ExportWithTax", "Outward", false, true, TaxDecision.Taxed)]
    [InlineData("Unregistered", "Regular", "Inward", true, false, TaxDecision.RefuseUnregisteredReverseCharge)]
    [InlineData("Composition", "Regular", "Inward", true, true, TaxDecision.Taxed)]
    [InlineData("Composition", "Regular", "Inward", false, true, TaxDecision.RefuseInputTax)]
    [InlineData("Composition", "Regular", "Outward", false, true, TaxDecision.TaxFree)]
    [InlineData("Unregistered", "Regular", "Inward", false, false, TaxDecision.TaxFree)]
    [InlineData("Unregistered", "Regular", "Outward", false, true, TaxDecision.TaxFree)]
    public void TaxMode_follows_the_host_rule(string reg, string kind, string dir, bool rc, bool taxed, TaxDecision expected) =>
        Assert.Equal(expected, TaxMode.Decide(Enum.Parse<GstRegistration>(reg), Enum.Parse<InvoiceKind>(kind),
            Enum.Parse<TradeDirection>(dir), rc, taxed));

    [Fact]
    public void RatesOn_lists_the_valid_slabs()
    {
        var rates = GstData.LoadEmbedded().RatesOn(new DateOnly(2026, 6, 1)).Select(r => r.BasisPoints);
        Assert.Equal([0, 10, 25, 100, 150, 300, 500, 600, 1200, 1800, 2800, 4000], rates);
    }
}
