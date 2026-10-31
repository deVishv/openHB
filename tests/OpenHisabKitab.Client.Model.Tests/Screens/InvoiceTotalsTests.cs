using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

/// <summary>Review Focus 4: what the screen shows equals what the host stores, to the paisa.</summary>
public sealed class InvoiceTotalsTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    async Task<(HostConnection C, int Company, CompanyProfileMsg Profile)> Company(string registration)
    {
        var c = await t.LoggedInOwnerAsync();
        var company = (await c.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = "Co " + registration, State = "27", Registration = registration, Gstin = registration == "Unregistered" ? "" : "27AAPFU0939F1ZV",
            Address = "Pune", FirstFyStartYear = 2026,
        }, o))).Company;
        if (registration != "Regular")
            await c.CallAsync((h, o) => h.Companies.SetSettingAsync(new SettingRequest { Company = company, Key = "invoice.roundOff", Json = "true" }, o));
        var profile = await c.CallAsync((h, o) => h.Companies.GetAsync(new CompanyRef { Company = company }, o));
        return (c, company, profile);
    }

    static async Task<(int Party, int Sales, int Purchases)> Books(HostConnection c, int company, string partyState, string kind)
    {
        var chart = await c.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o));
        var group = chart.Groups.Single(g => g.Kind == kind).Id;
        var party = (int)(await c.CallAsync((h, o) => h.Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = company, Name = "P" + partyState, Group = group, Party = new PartyMsg { State = partyState },
        }, o))).Id;
        return (party, chart.Ledgers.Single(l => l.Required == "Sales").Id, chart.Ledgers.Single(l => l.Required == "Purchases").Id);
    }

    [Theory]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", false, true, false, "1000.00", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "29", "29", false, true, false, "1000.00", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "04", "04", false, true, false, "999.99", "1800", "0", "3.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", true, true, false, "1120.00", "1200", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "ExportUnderLut", "27", "96", false, true, false, "1000.00", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "SezWithTax", "27", "27", false, true, false, "1000.00", "1800", "0", "1.000", "0.00")]
    [InlineData("Composition", "Sale", "Outward", "Regular", "27", "27", false, true, false, "1000.00", "0", "0", "1.000", "0.00")]
    [InlineData("Regular", "Purchase", "Inward", "Regular", "29", "27", false, true, true, "1000.00", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", false, false, false, "1000.33", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", false, true, false, "1000.33", "1800", "0", "1.000", "0.00")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", false, true, false, "1000.00", "500", "0", "1.000", "37.50")]
    [InlineData("Regular", "Sale", "Outward", "Regular", "27", "27", false, true, false, "123.45", "2800", "1200", "2.500", "0.00")]
    public async Task Client_totals_equal_host_totals(string registration, string type, string direction, string kind, string partyState, string pos,
        bool inclusive, bool roundOff, bool reverseCharge, string price, string rate, string cess, string qty, string discount)
    {
        var (c, company, profile) = await Company(registration);
        await c.CallAsync((h, o) => h.Companies.SetSettingAsync(new SettingRequest { Company = company, Key = "invoice.roundOff", Json = roundOff ? "true" : "false" }, o));
        var (party, sales, purchases) = await Books(c, company, partyState, direction == "Outward" ? "TradeReceivables" : "TradePayables");
        Assert.True(WireAmount.TryParseQuantity(qty, out var q));
        var draft = new InvoiceMsg
        {
            Type = type, Date = "2026-06-01", Party = party, Direction = direction, Kind = kind, PlaceOfSupply = pos, ReverseCharge = reverseCharge,
            Lines =
            {
                new InvoiceLineMsg
                {
                    Ledger = direction == "Outward" ? sales : purchases, Description = "Rod", HsnSac = "7214", QtyThousandths = q.Thousandths,
                    UnitPricePaise = Money.Parse(price).Paise, DiscountPaise = Money.Parse(discount).Paise, RateBp = int.Parse(rate, CultureInfo.InvariantCulture),
                    CessBp = int.Parse(cess, CultureInfo.InvariantCulture), PriceIncludesTax = inclusive,
                },
            },
        };
        var shown = InvoiceTotals.Calculate(profile, new StateCode(partyState), draft, roundOff);
        Assert.Null(shown.Problem);
        var saved = await c.CallAsync((h, o) => h.Vouchers.SaveInvoiceAsync(new InvoiceRequest { Company = company, Invoice = draft }, o));
        var stored = (await c.CallAsync((h, o) => h.Vouchers.GetAsync(new VoucherRef { Company = company, Id = saved.Id }, o))).Invoice;
        var lines = stored.Lines;
        Assert.Equal(lines.Sum(l => l.TaxablePaise), shown.Taxable.Paise);
        Assert.Equal(lines.Sum(l => l.CgstPaise), shown.Cgst.Paise);
        Assert.Equal(lines.Sum(l => l.SgstPaise), shown.Sgst.Paise);
        Assert.Equal(lines.Sum(l => l.UtgstPaise), shown.Utgst.Paise);
        Assert.Equal(lines.Sum(l => l.IgstPaise), shown.Igst.Paise);
        Assert.Equal(lines.Sum(l => l.CessPaise), shown.Cess.Paise);
        Assert.Equal(stored.RoundOffPaise, shown.RoundOff.Paise);
        var partyPosting = (await c.CallAsync((h, o) => h.Vouchers.GetAsync(new VoucherRef { Company = company, Id = saved.Id }, o))).Postings.Single(p => p.Ledger == party).Paise;
        Assert.Equal(Math.Abs(partyPosting), shown.Total.Paise);
    }

    [Fact]
    public void A_line_that_cannot_be_calculated_says_why_in_host_words()
    {
        var profile = new CompanyProfileMsg { State = "27", Registration = "Regular" };
        var draft = new InvoiceMsg
        {
            Type = "Sale", Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
            Lines = { new InvoiceLineMsg { QtyThousandths = 0, UnitPricePaise = 100 } },
        };
        Assert.Equal("Line 1: quantity must be more than zero.", InvoiceTotals.Calculate(profile, new StateCode("27"), draft, true).Problem);
    }
}
