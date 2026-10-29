using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace OpenHisabKitab.Host.Tests.Api;

public sealed class RestWriteTests : IDisposable
{
    readonly ApiBooks b = new();
    readonly HttpClient rw;
    readonly int co;

    public RestWriteTests()
    {
        rw = b.Client(readWrite: true);
        co = b.Company;
    }

    public void Dispose() => b.Dispose();

    static async Task<(HttpStatusCode Status, string Message)> Error(HttpResponseMessage r) =>
        (r.StatusCode, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("message").GetString()!);

    object Receipt(string amount = "12345.60", string date = "2026-05-10") => new
    {
        type = "receipt", date, narration = "cash sale",
        lines = new[] { new { ledgerId = b.Cash.Value, amount }, new { ledgerId = b.Sales.Value, amount = "-" + amount } },
    };

    Task<HttpResponseMessage> PostJson(HttpClient c, string path, string body) =>
        c.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    int AuditCount() => b.Admin.Audit(b.Owner, co, new AuditFilter()).Count;

    [Fact]
    public async Task Journal_saved_over_rest_gets_its_number_and_is_audited_as_rest_with_the_token_name()
    {
        var r = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/journal", Receipt());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var saved = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RCT/26-27/00001", saved.GetProperty("number").GetString());
        Assert.Equal(1, saved.GetProperty("rowVersion").GetInt64());
        Assert.Equal($"/api/v1/companies/{co}/vouchers/{saved.GetProperty("id").GetInt64()}", r.Headers.Location?.OriginalString);
        var audit = b.Admin.Audit(b.Owner, co, new AuditFilter(Entity: "voucher")).OrderBy(e => e.Seq).Last();
        Assert.Equal((ChangeSource.Rest, "rw-script", "owner", Environment.MachineName),
            (audit.Entry.Source, audit.Entry.TokenName, audit.Entry.User, audit.Entry.Machine));
    }

    [Theory]
    [InlineData("\"12345.6\"", "$.lines[0].amount must be a string like \"12345.60\".")]
    [InlineData("\"12,345.60\"", "$.lines[0].amount must be a string like \"12345.60\".")]
    [InlineData("12345.60", "Check $.lines[0].amount in the request body.")]
    public async Task Amount_in_the_wrong_form_is_400_and_uses_no_number(string amount, string message)
    {
        var body = $$"""{"type":"receipt","date":"2026-05-10","narration":"","lines":[{"ledgerId":{{b.Cash.Value}},"amount":{{amount}}},{"ledgerId":{{b.Sales.Value}},"amount":"-12345.60"}]}""";
        Assert.Equal((HttpStatusCode.BadRequest, message), await Error(await PostJson(rw, $"/api/v1/companies/{co}/vouchers/journal", body)));
        Assert.Empty(b.Vouchers.List(b.Owner, co, ApiBooks.Fy2026, null));
        var ok = await (await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/journal", Receipt())).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RCT/26-27/00001", ok.GetProperty("number").GetString());
    }

    [Theory]
    [InlineData("""{"type":"receipt","date":"10-05-2026","narration":"","lines":[]}""", "$.date must look like 2026-04-01.")]
    [InlineData("""{"type":"Receipt","date":"2026-05-10","narration":"","lines":[]}""", "$.type must be one of sale, purchase, creditNote, debitNote, receipt, payment, journal, contra.")]
    [InlineData("""{"type":"receipt","date":"2026-05-10","narration":"","lines":[],"typo":1}""", "Check $.typo in the request body.")]
    [InlineData("""{"type":"receipt","date":"2026-05-10","lines":[]}""", "Add the missing fields to the request body.")]
    [InlineData("""{"type":"receipt",""", "The request body is not valid JSON.")]
    public async Task Malformed_bodies_are_400(string body, string message)
    {
        Assert.Equal((HttpStatusCode.BadRequest, message), await Error(await PostJson(rw, $"/api/v1/companies/{co}/vouchers/journal", body)));
    }

    [Fact]
    public async Task Read_only_token_write_is_403_with_its_reason_and_writes_nothing()
    {
        var before = AuditCount();
        var ro = b.Client(readWrite: false);
        Assert.Equal((HttpStatusCode.Forbidden, "This token is read only. Create a read and write token to make changes."),
            await Error(await ro.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/journal", Receipt())));
        Assert.Empty(b.Vouchers.List(b.Owner, co, ApiBooks.Fy2026, null));
        Assert.Equal(before, AuditCount());
    }

    [Fact]
    public async Task Stale_row_version_is_409_with_the_current_voucher()
    {
        var saved = b.Receipt("100.00");
        var edit = new { rowVersion = saved.RowVersion, voucher = Receipt("200.00") };
        var first = await rw.PutAsJsonAsync($"/api/v1/companies/{co}/vouchers/{saved.Id}/journal", edit);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(2, (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetInt64());

        var second = await rw.PutAsJsonAsync($"/api/v1/companies/{co}/vouchers/{saved.Id}/journal", edit);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal(("conflict", "Someone else changed this voucher. Showing the current version."),
            (error.GetProperty("code").GetString(), error.GetProperty("message").GetString()));
        var current = error.GetProperty("current");
        Assert.Equal(2, current.GetProperty("rowVersion").GetInt64());
        var cash = current.GetProperty("postings").EnumerateArray().Single(p => p.GetProperty("ledger").GetString() == "Cash");
        Assert.Equal("200.00", cash.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Cancel_over_rest_keeps_the_number()
    {
        var saved = b.Receipt("100.00");
        var r = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/{saved.Id}/cancel", new { rowVersion = saved.RowVersion, reason = "typed twice" });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var v = b.Vouchers.Get(b.Owner, co, saved.Id);
        Assert.True(v.Cancelled);
        Assert.Equal(saved.DisplayNumber, v.DisplayNumber);
    }

    [Fact]
    public async Task Unbalanced_journal_message_comes_from_the_service()
    {
        var input = new JournalInput(VoucherType.Receipt, new DateOnly(2026, 5, 10), "",
            [new PostingLine(b.Cash, Money.Parse("100.00")), new PostingLine(b.Sales, Money.Parse("-99.00"))]);
        var expected = (await Assert.ThrowsAsync<ServiceException>(() => b.Vouchers.SaveJournalAsync(b.Owner, co, input))).Message;
        var body = new { type = "receipt", date = "2026-05-10", narration = "",
            lines = new[] { new { ledgerId = b.Cash.Value, amount = "100.00" }, new { ledgerId = b.Sales.Value, amount = "-99.00" } } };
        Assert.Equal((HttpStatusCode.BadRequest, expected), await Error(await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/journal", body)));
    }

    [Fact]
    public async Task Invoice_over_rest_matches_the_same_invoice_saved_through_the_service()
    {
        var direct = b.Vouchers.Get(b.Owner, co, b.Sale("400.00").Id);
        var body = new
        {
            type = "sale", date = "2026-06-01", narration = "", partyId = b.Customer.Value, direction = "outward", kind = "regular",
            placeOfSupply = "27", reverseCharge = false,
            lines = new[] { new { itemId = (int?)null, ledgerId = b.Sales.Value, description = "Steel rod", hsnSac = "7214", quantity = "2.500",
                unitPrice = "400.00", discount = "0.00", rate = "18.00", cessRate = "0.00", priceIncludesTax = false } },
        };
        var r = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/vouchers/invoice", body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var saved = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INV/26-27/00002", saved.GetProperty("number").GetString());
        var viaRest = b.Vouchers.Get(b.Owner, co, saved.GetProperty("id").GetInt64());
        Assert.Equal(direct.Postings, viaRest.Postings);
        Assert.Equal(direct.Invoice!.Lines, viaRest.Invoice!.Lines);
        Assert.Equal(direct.Invoice.RoundOff, viaRest.Invoice.RoundOff);
    }

    [Fact]
    public async Task Masters_are_created_and_renamed_over_rest()
    {
        var chart = b.Masters.Chart(b.Owner, co);
        var expenses = chart.Group(GroupKind.OtherExpenses).Value;
        var group = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/groups", new { name = "Office", parentId = expenses });
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        var groupId = (await group.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var ledger = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/ledgers", new { name = "Tea", groupId });
        var ledgerId = (await ledger.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await rw.PutAsJsonAsync($"/api/v1/companies/{co}/ledgers/{ledgerId}/name", new { name = "Tea and snacks" })).StatusCode);
        Assert.Equal("Tea and snacks", b.Masters.Chart(b.Owner, co).Ledger(new LedgerId(ledgerId)).Name);

        var item = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/items",
            new { name = "Rod", hsnSac = "7214", isService = false, unit = "kg", rate = "18.00", cessRate = "0.00", defaultPrice = "55.50" });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        var stored = Assert.Single(b.Masters.Items(b.Owner, co));
        Assert.Equal((Money.Parse("55.50"), 1800), (stored.DefaultPrice, stored.Rate.BasisPoints));
    }

    [Fact]
    public async Task Party_edit_with_a_stale_row_version_is_409_with_the_current_party()
    {
        var receivables = b.Masters.Chart(b.Owner, co).Group(GroupKind.TradeReceivables).Value;
        var create = await rw.PostAsJsonAsync($"/api/v1/companies/{co}/parties",
            new { name = "Blr Mart", groupId = receivables, gstin = (string?)null, state = "29", creditDays = 15, address = "Bengaluru" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var edit = new { name = "Blr Mart", groupId = receivables, gstin = (string?)null, state = "29", creditDays = 30, address = "Bengaluru", rowVersion = 1 };
        Assert.Equal(HttpStatusCode.OK, (await rw.PutAsJsonAsync($"/api/v1/companies/{co}/parties/{id}", edit)).StatusCode);
        var stale = await rw.PutAsJsonAsync($"/api/v1/companies/{co}/parties/{id}", edit);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var current = (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("current");
        Assert.Equal(("Blr Mart", 30, 2L), (current.GetProperty("name").GetString(), current.GetProperty("creditDays").GetInt32(), current.GetProperty("rowVersion").GetInt64()));
    }

    [Fact]
    public async Task Bad_state_and_gstin_are_400()
    {
        var receivables = b.Masters.Chart(b.Owner, co).Group(GroupKind.TradeReceivables).Value;
        Assert.Equal((HttpStatusCode.BadRequest, "Unknown state code: 99."), await Error(await rw.PostAsJsonAsync($"/api/v1/companies/{co}/parties",
            new { name = "X", groupId = receivables, gstin = (string?)null, state = "99", creditDays = 0, address = "" })));
        Assert.Equal((HttpStatusCode.BadRequest, "GSTIN is not valid."), await Error(await rw.PostAsJsonAsync($"/api/v1/companies/{co}/parties",
            new { name = "X", groupId = receivables, gstin = "27AAAAA0000A1Z0", state = "27", creditDays = 0, address = "" })));
    }
}
