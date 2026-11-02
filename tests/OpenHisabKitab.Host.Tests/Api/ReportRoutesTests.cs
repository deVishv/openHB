using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenHisabKitab.Host.Tests.Api;

/// <summary>The Plan 6 report routes: layouts, cash flow, and outstanding.</summary>
public sealed class ReportRoutesTests : IDisposable
{
    readonly ApiBooks b = new();
    readonly HttpClient api;
    readonly int co;

    public ReportRoutesTests()
    {
        api = b.Client();
        co = b.Company;
    }

    public void Dispose() => b.Dispose();

    string R(string path) => $"/api/v1/companies/{co}/reports/{path}";

    static async Task<(HttpStatusCode, string)> Error(HttpResponseMessage r) =>
        (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString()!);

    [Fact]
    public async Task Cash_flow_route_amounts_are_strings()
    {
        b.Receipt("1000.00");
        var cf = ApiBooks.Json(await api.GetStringAsync(R("cash-flow?from=2026-04-01&to=2027-03-31")));
        Assert.Equal("1000.00", cf.GetProperty("netChange").GetString());
        Assert.Equal("1000.00", cf.GetProperty("closingCash").GetString());
        var line = cf.GetProperty("lines")[0];
        Assert.Equal(("operating", "netProfit", "Net profit", "1000.00"),
            (line.GetProperty("section").GetString(), line.GetProperty("item").GetString(), line.GetProperty("label").GetString(),
             line.GetProperty("amount").GetString()));
    }

    [Fact]
    public async Task Outstanding_route_reads_side_and_as_at()
    {
        b.BillSale("700.00");
        var outs = ApiBooks.Json(await api.GetStringAsync(R("outstanding?asAt=2026-07-31&side=receivable")));
        Assert.Equal(("2026-07-31", "receivable", "700.00"),
            (outs.GetProperty("asAt").GetString(), outs.GetProperty("side").GetString(), outs.GetProperty("total").GetString()));
        var party = outs.GetProperty("parties")[0];
        Assert.Equal(("Ravi Stores", "700.00"), (party.GetProperty("party").GetString(), party.GetProperty("total").GetString()));
        var bill = party.GetProperty("bills")[0];
        Assert.Equal(("INV/26-27/00001", "2026-07-01", 30, 1),
            (bill.GetProperty("number").GetString(), bill.GetProperty("due").GetString(), bill.GetProperty("daysOverdue").GetInt32(),
             bill.GetProperty("bucket").GetInt32()));
        Assert.Equal(new[] { 30, 60, 90 }, outs.GetProperty("ageingDays").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal("sale", bill.GetProperty("voucherType").GetString());

        var payables = ApiBooks.Json(await api.GetStringAsync(R("outstanding?asAt=2026-07-31&side=payable")));
        Assert.Equal(0, payables.GetProperty("parties").GetArrayLength());
        var byDefault = ApiBooks.Json(await api.GetStringAsync(R("outstanding?asAt=2026-07-31")));
        Assert.Equal("receivable", byDefault.GetProperty("side").GetString());
    }

    [Fact]
    public async Task Layout_route_defaults_to_schedule_iii()
    {
        b.Receipt("1000.00");
        var bs = ApiBooks.Json(await api.GetStringAsync(R("balance-sheet/layout?asAt=2027-03-31")));
        Assert.Equal(("scheduleIII", false), (bs.GetProperty("style").GetString(), bs.GetProperty("hasPrevious").GetBoolean()));
        var cash = bs.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("kind").GetString() == "cashAndEquivalents");
        Assert.Equal(("Cash and cash equivalents", "1000.00", JsonValueKind.Null),
            (cash.GetProperty("label").GetString(), cash.GetProperty("current").GetString(), cash.GetProperty("previous").ValueKind));
        Assert.Contains(bs.GetProperty("lines").EnumerateArray(),
            l => l.GetProperty("kind").GetString() == "headingTotal" && l.GetProperty("label").GetString() == "Total for Current Assets");

        var pl = ApiBooks.Json(await api.GetStringAsync(R("profit-and-loss/layout?from=2026-04-01&to=2027-03-31&style=chart")));
        Assert.Equal("chart", pl.GetProperty("style").GetString());
        Assert.Contains(pl.GetProperty("lines").EnumerateArray(), l => l.GetProperty("kind").GetString() == "ledger" && l.GetProperty("label").GetString() == "Sales");
    }

    [Theory]
    [InlineData("outstanding?asAt=2026-07-31&side=both", "side must be one of receivable, payable.")]
    [InlineData("balance-sheet/layout?asAt=2027-03-31&style=tally", "style must be one of scheduleIII, chart.")]
    [InlineData("cash-flow?from=2026-06-01&to=2027-05-31", "Choose a period within one financial year.")]
    public async Task Bad_arguments_are_400_with_one_line(string path, string message) =>
        Assert.Equal((HttpStatusCode.BadRequest, message), await Error(await api.GetAsync(R(path))));

    [Theory]
    [InlineData("cash-flow?from=2026-04-01&to=2027-03-31")]
    [InlineData("outstanding?asAt=2026-07-31")]
    [InlineData("balance-sheet/layout?asAt=2027-03-31")]
    [InlineData("profit-and-loss/layout?from=2026-04-01&to=2027-03-31")]
    public async Task Data_entry_token_gets_403_on_every_report_route(string path)
    {
        var clerk = b.F.ApiClient(b.F.NewUserToken("clerk", BuiltInRole.DataEntry, false, co));
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync(R(path))).StatusCode);
    }
}
