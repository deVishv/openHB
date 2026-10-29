using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OpenHisabKitab.Host.Tests.Api;

public sealed class RestReadTests : IDisposable
{
    readonly ApiBooks b = new();
    readonly HttpClient api;
    readonly int co;

    public RestReadTests()
    {
        api = b.Client();
        co = b.Company;
    }

    public void Dispose() => b.Dispose();

    static async Task<(HttpStatusCode, string)> Error(HttpResponseMessage r) =>
        (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString()!);

    [Fact]
    public async Task Trial_balance_amounts_are_strings_with_two_decimals_and_ledger_names()
    {
        b.Receipt("12345.60");
        var json = await api.GetStringAsync($"/api/v1/companies/{co}/reports/trial-balance?from=2026-04-01&to=2027-03-31");
        var tb = ApiBooks.Json(json);
        var cash = tb.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("ledger").GetString() == "Cash");
        Assert.Equal(b.Cash.Value, cash.GetProperty("ledgerId").GetInt32());
        Assert.Equal(JsonValueKind.String, cash.GetProperty("closing").ValueKind);
        Assert.Equal("12345.60", cash.GetProperty("closing").GetString());
        Assert.Equal("12345.60", tb.GetProperty("totalDebit").GetString());
        Assert.Equal(tb.GetProperty("totalDebit").GetString(), tb.GetProperty("totalCredit").GetString());
        Assert.Equal("2026-04-01", tb.GetProperty("from").GetString());
        Assert.DoesNotContain("paise", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Other_reports_read_the_same_books()
    {
        b.Receipt("1000.00");
        var pl = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/reports/profit-and-loss?from=2026-04-01&to=2027-03-31"));
        Assert.Equal("1000.00", pl.GetProperty("netProfit").GetString());
        Assert.Equal("Sales", pl.GetProperty("income")[0].GetProperty("ledger").GetString());
        var bs = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/reports/balance-sheet?asAt=2027-03-31"));
        Assert.Equal(bs.GetProperty("totalAssets").GetString(), bs.GetProperty("totalLiabilities").GetString());
        Assert.Equal("1000.00", bs.GetProperty("currentProfit").GetString());
        var st = ApiBooks.Json(await api.GetStringAsync(
            $"/api/v1/companies/{co}/reports/ledger-statement?ledger={b.Cash.Value}&from=2026-04-01&to=2027-03-31"));
        Assert.Equal(("0.00", "1000.00"), (st.GetProperty("opening").GetString(), st.GetProperty("closing").GetString()));
        var row = st.GetProperty("rows").EnumerateArray().Single();
        Assert.Equal(("2026-05-10", "1000.00", "1000.00"), (row.GetProperty("date").GetString(), row.GetProperty("amount").GetString(), row.GetProperty("balance").GetString()));
        Assert.Equal("RCT/26-27/00001", row.GetProperty("voucherNumber").GetString());
    }

    [Fact]
    public async Task Companies_years_groups_ledgers_parties_items()
    {
        var companies = ApiBooks.Json(await api.GetStringAsync("/api/v1/companies"));
        Assert.Equal((co, "A Traders"), (companies[0].GetProperty("id").GetInt32(), companies[0].GetProperty("name").GetString()));
        var detail = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}"));
        Assert.Equal(("27", "regular", "27AAPFU0939F1ZV"), (detail.GetProperty("state").GetString(), detail.GetProperty("registration").GetString(), detail.GetProperty("gstin").GetString()));
        var years = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/years"));
        Assert.Equal((2026, "2026-04-01", "2027-03-31"), (years[0].GetProperty("startYear").GetInt32(), years[0].GetProperty("from").GetString(), years[0].GetProperty("to").GetString()));
        var groups = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/groups"));
        Assert.Contains(groups.EnumerateArray(), g => g.GetProperty("nature").GetString() == "asset");
        var ledgers = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/ledgers"));
        var cash = ledgers.EnumerateArray().Single(l => l.GetProperty("id").GetInt32() == b.Cash.Value);
        Assert.Equal(("Cash", "cash", "0.00"), (cash.GetProperty("name").GetString(), cash.GetProperty("required").GetString(), cash.GetProperty("opening").GetString()));
        var parties = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/parties"));
        Assert.Equal(("Ravi Stores", "27", 30), (parties[0].GetProperty("name").GetString(), parties[0].GetProperty("state").GetString(), parties[0].GetProperty("creditDays").GetInt32()));
        Assert.Equal(JsonValueKind.Array, ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/items")).ValueKind);
    }

    [Fact]
    public async Task Lists_only_companies_the_token_user_can_open()
    {
        var other = b.F.Get<CompanyService>().Create(b.Owner, TestHost.Profile("B Traders"));
        var clerk = b.F.ApiClient(b.F.NewUserToken("clerk", BuiltInRole.DataEntry, false, co));
        var list = ApiBooks.Json(await clerk.GetStringAsync("/api/v1/companies"));
        Assert.Equal([co], list.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()));
        Assert.Equal((HttpStatusCode.Forbidden, "You do not have permission for this."), await Error(await clerk.GetAsync($"/api/v1/companies/{other}")));
    }

    [Fact]
    public async Task Voucher_read_has_number_row_version_and_signed_postings()
    {
        var saved = b.Receipt("12345.60");
        var v = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/vouchers/{saved.Id}"));
        Assert.Equal(("receipt", "RCT/26-27/00001", "2026-05-10", 1L), (v.GetProperty("type").GetString(), v.GetProperty("number").GetString(), v.GetProperty("date").GetString(), v.GetProperty("rowVersion").GetInt64()));
        var postings = v.GetProperty("postings").EnumerateArray().ToDictionary(p => p.GetProperty("ledger").GetString()!, p => p.GetProperty("amount").GetString());
        Assert.Equal("12345.60", postings["Cash"]);
        Assert.Equal("-12345.60", postings["Sales"]);
        Assert.Equal(JsonValueKind.Null, v.GetProperty("invoice").ValueKind);
    }

    [Fact]
    public async Task Invoice_read_has_lines_with_wire_amounts()
    {
        var saved = b.Sale("400.00");
        var v = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/vouchers/{saved.Id}"));
        var inv = v.GetProperty("invoice");
        Assert.Equal(("Ravi Stores", "outward", "regular", "27"), (inv.GetProperty("party").GetString(), inv.GetProperty("direction").GetString(), inv.GetProperty("kind").GetString(), inv.GetProperty("placeOfSupply").GetString()));
        var line = inv.GetProperty("lines")[0];
        Assert.Equal(("2.500", "400.00", "18.00", "1000.00", "90.00"),
            (line.GetProperty("quantity").GetString(), line.GetProperty("unitPrice").GetString(), line.GetProperty("rate").GetString(),
             line.GetProperty("taxable").GetString(), line.GetProperty("cgst").GetString()));
    }

    [Fact]
    public async Task Voucher_list_filters_by_dates_and_type()
    {
        b.Receipt("10.00");
        b.Sale("100.00");
        var all = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/vouchers?from=2026-04-01&to=2027-03-31"));
        Assert.Equal(2, all.GetArrayLength());
        var sales = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/vouchers?from=2026-04-01&to=2027-03-31&type=sale"));
        Assert.Equal("sale", Assert.Single(sales.EnumerateArray()).GetProperty("type").GetString());
        Assert.Equal((HttpStatusCode.BadRequest, "type must be one of sale, purchase, creditNote, debitNote, receipt, payment, journal, contra."),
            await Error(await api.GetAsync($"/api/v1/companies/{co}/vouchers?from=2026-04-01&to=2027-03-31&type=Sales")));
    }

    [Fact]
    public async Task Data_entry_token_cannot_read_reports()
    {
        var clerk = b.F.ApiClient(b.F.NewUserToken("clerk", BuiltInRole.DataEntry, false, co));
        Assert.Equal((HttpStatusCode.Forbidden, "You do not have permission for this."),
            await Error(await clerk.GetAsync($"/api/v1/companies/{co}/reports/trial-balance?from=2026-04-01&to=2027-03-31")));
    }

    [Fact]
    public async Task Data_entry_token_lists_only_own_vouchers()
    {
        b.Receipt("10.00");
        var clerk = b.F.ApiClient(b.F.NewUserToken("clerk", BuiltInRole.DataEntry, false, co));
        Assert.Equal(0, ApiBooks.Json(await clerk.GetStringAsync($"/api/v1/companies/{co}/vouchers?from=2026-04-01&to=2027-03-31")).GetArrayLength());
    }

    [Theory]
    [InlineData("from=01-04-2026&to=2027-03-31", "from must look like 2026-04-01.")]
    [InlineData("from=2026-4-1&to=2027-03-31", "from must look like 2026-04-01.")]
    [InlineData("from=2026-04-01&to=2027-3-31", "to must look like 2026-04-01.")]
    [InlineData("to=2027-03-31", "Add from.")]
    [InlineData("from=2026-04-01", "Add to.")]
    [InlineData("from=2026-05-01&to=2026-04-01", "from must be on or before to.")]
    public async Task Bad_dates_are_400(string query, string message)
    {
        Assert.Equal((HttpStatusCode.BadRequest, message), await Error(await api.GetAsync($"/api/v1/companies/{co}/reports/trial-balance?{query}")));
    }

    [Fact]
    public async Task Ledger_and_party_search_use_the_name_matcher()
    {
        var ledgers = ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/ledgers?search=cas"));
        Assert.Contains(ledgers.EnumerateArray(), l => l.GetProperty("name").GetString() == "Cash");
        Assert.DoesNotContain(ledgers.EnumerateArray(), l => l.GetProperty("name").GetString() == "Sales");
        Assert.Equal(1, ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/parties?search=ravi")).GetArrayLength());
        Assert.Equal(0, ApiBooks.Json(await api.GetStringAsync($"/api/v1/companies/{co}/parties?search=zzz")).GetArrayLength());
    }

    [Fact]
    public async Task Unknown_voucher_is_404()
    {
        var r = await api.GetAsync($"/api/v1/companies/{co}/vouchers/999");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());
    }
}
