using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenHisabKitab.Host.Grpc;
using OpenHisabKitab.Host.Tests.Api;

namespace OpenHisabKitab.Host.Tests.Mcp;

public sealed class McpToolTests : IDisposable
{
    readonly ApiBooks b = new(mcp: true);
    readonly int co;

    public McpToolTests() => co = b.Company;

    public void Dispose() => b.Dispose();

    HttpClient Client(bool readWrite = false, string name = "claude") => b.F.ApiClient(b.F.NewToken(readWrite, name));

    /// <summary>Posts one JSON-RPC request to /mcp and returns its result, read from the event stream reply.</summary>
    static async Task<JsonElement> Rpc(HttpClient c, string method, object @params)
    {
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await c.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        var data = text.Split('\n').Select(l => l.TrimEnd('\r')).Single(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..];
        return JsonDocument.Parse(data).RootElement.GetProperty("result").Clone();
    }

    static Task<JsonElement> Call(HttpClient c, string tool, object args) => Rpc(c, "tools/call", new { name = tool, arguments = args });

    static string Text(JsonElement result) => result.GetProperty("content")[0].GetProperty("text").GetString()!;

    static JsonElement Ok(JsonElement result)
    {
        Assert.False(result.TryGetProperty("isError", out var e) && e.GetBoolean(), Text(result));
        return ApiBooks.Json(Text(result));
    }

    static string Error(JsonElement result)
    {
        Assert.True(result.GetProperty("isError").GetBoolean());
        return Text(result);
    }

    object Receipt(string amount) => new
    {
        type = "receipt", date = "2026-05-10", narration = "from the assistant",
        lines = new object[] { new { ledgerId = b.Cash.Value, amount }, new { ledgerId = b.Sales.Value, amount = "-100.00" } },
    };

    [Fact]
    public async Task Initialize_reports_the_host_version()
    {
        var init = await Rpc(Client(), "initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "t", version = "1" } });
        Assert.Equal(("Open Hisab Kitab", HostVersion.Current),
            (init.GetProperty("serverInfo").GetProperty("name").GetString(), init.GetProperty("serverInfo").GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Trial_balance_amounts_carry_wire_value_and_indian_text()
    {
        b.Receipt("1234567.80");
        var r = await Call(Client(), "trial_balance", new { company = co, from = "2026-04-01", to = "2027-03-31" });
        var text = Text(r);
        Assert.Contains("""{"amount":"1234567.80","text":"12,34,567.80"}""", text, StringComparison.Ordinal);
        Assert.DoesNotContain("paise", text, StringComparison.OrdinalIgnoreCase);
        var tb = Ok(r);
        Assert.Equal("1234567.80", tb.GetProperty("totalDebit").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Read_tools_answer_from_the_books()
    {
        b.Receipt("500.00");
        var api = Client();
        var companies = Ok(await Call(api, "list_companies", new { }));
        Assert.Equal(("A Traders", 2026), (companies[0].GetProperty("name").GetString(), companies[0].GetProperty("years")[0].GetProperty("startYear").GetInt32()));
        var hits = Ok(await Call(api, "search_ledgers", new { company = co, query = "ravi" })).GetProperty("rows");
        var ravi = Assert.Single(hits.EnumerateArray());
        Assert.Equal(("Ravi Stores", "27", true), (ravi.GetProperty("name").GetString(), ravi.GetProperty("state").GetString(), ravi.GetProperty("isParty").GetBoolean()));
        var st = Ok(await Call(api, "ledger_statement", new { company = co, ledgerId = b.Cash.Value, from = "2026-04-01", to = "2027-03-31" }));
        Assert.Equal("500.00", st.GetProperty("closing").GetProperty("amount").GetString());
        Assert.Equal("RCT/26-27/00001", st.GetProperty("rows")[0].GetProperty("voucherNumber").GetString());
        var pl = Ok(await Call(api, "profit_and_loss", new { company = co, from = "2026-04-01", to = "2027-03-31" }));
        Assert.Equal("500.00", pl.GetProperty("netProfit").GetProperty("amount").GetString());
        var bs = Ok(await Call(api, "balance_sheet", new { company = co, asAt = "2027-03-31" }));
        Assert.Equal(bs.GetProperty("totalAssets").GetProperty("amount").GetString(), bs.GetProperty("totalLiabilities").GetProperty("amount").GetString());
        var found = Ok(await Call(api, "find_vouchers", new { company = co, from = "2026-04-01", to = "2027-03-31", text = "CASH" }));
        var summary = Assert.Single(found.GetProperty("rows").EnumerateArray());
        Assert.Equal(("RCT/26-27/00001", "500.00"), (summary.GetProperty("number").GetString(), summary.GetProperty("amount").GetProperty("amount").GetString()));
        var v = Ok(await Call(api, "get_voucher", new { company = co, id = summary.GetProperty("id").GetInt64() }));
        Assert.Equal("receipt", v.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Cash_flow_outstanding_and_layouts_return_indian_text()
    {
        b.Receipt("1234567.80");
        b.BillSale("700.00");
        var api = Client();
        var cf = await Call(api, "cash_flow", new { company = co, from = "2026-04-01", to = "2027-03-31" });
        Assert.Contains("""{"amount":"1234567.80","text":"12,34,567.80"}""", Text(cf), StringComparison.Ordinal);
        Assert.Equal("1234567.80", Ok(cf).GetProperty("closingCash").GetProperty("amount").GetString());

        var outs = Ok(await Call(api, "outstanding", new { company = co, asAt = "2026-07-31" }));
        Assert.Equal("700.00", outs.GetProperty("total").GetProperty("amount").GetString());
        var payable = Ok(await Call(api, "outstanding", new { company = co, asAt = "2026-07-31", side = "payable" }));
        Assert.Equal(0, payable.GetProperty("parties").GetArrayLength());

        var bs = Ok(await Call(api, "balance_sheet_layout", new { company = co, asAt = "2027-03-31" }));
        Assert.Equal("scheduleIII", bs.GetProperty("style").GetString());
        var pl = Ok(await Call(api, "profit_and_loss_layout", new { company = co, from = "2026-04-01", to = "2027-03-31", style = "chart" }));
        Assert.Equal("chart", pl.GetProperty("style").GetString());
    }

    [Theory]
    [InlineData("outstanding")]
    [InlineData("cash_flow")]
    [InlineData("balance_sheet_layout")]
    [InlineData("profit_and_loss_layout")]
    public async Task New_report_tools_are_refused_for_data_entry(string tool)
    {
        var clerk = b.F.ApiClient(b.F.NewUserToken("clerk", BuiltInRole.DataEntry, false, co));
        object args = tool switch
        {
            "outstanding" or "balance_sheet_layout" => new { company = co, asAt = "2026-07-31" },
            _ => new { company = co, from = "2026-04-01", to = "2027-03-31" },
        };
        Assert.Equal("You do not have permission for this.", Error(await Call(clerk, tool, args)));
    }

    [Fact]
    public async Task Service_refusal_reaches_the_assistant_word_for_word()
    {
        var viewer = b.F.ApiClient(b.F.NewUserToken("viewer", BuiltInRole.Viewer, true, co));
        Assert.Equal("You do not have permission for this.", Error(await Call(viewer, "create_voucher", new { company = co, voucher = Receipt("100.00") })));
    }

    [Fact]
    public async Task Writes_are_refused_until_allowWrites_and_nothing_is_saved()
    {
        var before = b.Admin.Audit(b.Owner, co, new AuditFilter()).Count;
        Assert.Equal("Changes through MCP are off. The Owner can turn on mcp.allowWrites.",
            Error(await Call(Client(readWrite: true), "create_voucher", new { company = co, voucher = Receipt("100.00") })));
        Assert.Empty(b.Vouchers.List(b.Owner, co, ApiBooks.Fy2026, null));
        Assert.Equal(before, b.Admin.Audit(b.Owner, co, new AuditFilter()).Count);
    }

    [Fact]
    public async Task Read_only_token_cannot_write_even_with_allowWrites()
    {
        b.F.Set("mcp.allowWrites", "true");
        Assert.Equal("This token is read only. Create a read and write token to make changes.",
            Error(await Call(Client(readWrite: false), "create_voucher", new { company = co, voucher = Receipt("100.00") })));
    }

    [Fact]
    public async Task Created_voucher_is_audited_as_mcp_with_the_token_name()
    {
        b.F.Set("mcp.allowWrites", "true");
        var saved = Ok(await Call(Client(readWrite: true, name: "claude"), "create_voucher", new { company = co, voucher = Receipt("100.00") }));
        Assert.Equal("RCT/26-27/00001", saved.GetProperty("number").GetString());
        var audit = b.Admin.Audit(b.Owner, co, new AuditFilter(Entity: "voucher")).OrderBy(e => e.Seq).Last();
        Assert.Equal((ChangeSource.Mcp, "claude"), (audit.Entry.Source, audit.Entry.TokenName));
    }

    [Fact]
    public async Task Invoice_and_party_are_created_with_writes_on()
    {
        b.F.Set("mcp.allowWrites", "true");
        var api = Client(readWrite: true);
        var receivables = b.Masters.Chart(b.Owner, co).Group(GroupKind.TradeReceivables).Value;
        var party = Ok(await Call(api, "create_party", new { company = co, party = new { name = "Blr Mart", groupId = receivables, gstin = (string?)null, state = "29", creditDays = 0, address = "" } }));
        var invoice = new
        {
            type = "sale", date = "2026-06-01", narration = "", partyId = party.GetProperty("id").GetInt32(), direction = "outward", kind = "regular",
            placeOfSupply = "29", reverseCharge = false,
            lines = new[] { new { itemId = (int?)null, ledgerId = b.Sales.Value, description = "Rod", hsnSac = "7214", quantity = "1.000",
                unitPrice = "1000.00", discount = "0.00", rate = "18.00", cessRate = "0.00", priceIncludesTax = false } },
        };
        var saved = Ok(await Call(api, "create_invoice", new { company = co, invoice }));
        var v = b.Vouchers.Get(b.Owner, co, saved.GetProperty("id").GetInt64());
        Assert.Equal(Money.Parse("180.00"), v.Invoice!.Lines.Single().Igst);
    }

    [Fact]
    public async Task Wrong_amounts_save_nothing()
    {
        b.F.Set("mcp.allowWrites", "true");
        var api = Client(readWrite: true);
        Assert.Equal("$.voucher.lines[0].amount must be a string like \"12345.60\".",
            Error(await Call(api, "create_voucher", new { company = co, voucher = Receipt("12345.6") })));
        var asNumber = new
        {
            type = "receipt", date = "2026-05-10", narration = "",
            lines = new object[] { new { ledgerId = b.Cash.Value, amount = 100.00m }, new { ledgerId = b.Sales.Value, amount = "-100.00" } },
        };
        Assert.Equal("$.voucher.lines[0].amount must be a string like \"12345.60\".", Error(await Call(api, "create_voucher", new { company = co, voucher = asNumber })));
        Assert.Empty(b.Vouchers.List(b.Owner, co, ApiBooks.Fy2026, null));
    }

    [Fact]
    public async Task Find_vouchers_caps_rows_and_says_how_many_were_left_out()
    {
        for (var i = 0; i < 105; i++) b.Receipt("1.00");
        var found = Ok(await Call(Client(), "find_vouchers", new { company = co, from = "2026-04-01", to = "2027-03-31" }));
        Assert.Equal(100, found.GetProperty("rows").GetArrayLength());
        Assert.Equal(5, found.GetProperty("omitted").GetInt32());
        Assert.Equal("5 more rows. Narrow the dates or the search.", found.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Tools_list_has_every_tool_with_string_amount_schemas()
    {
        var tools = (await Rpc(Client(), "tools/list", new { })).GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(
            ["balance_sheet", "balance_sheet_layout", "cash_flow", "create_invoice", "create_party", "create_voucher", "find_vouchers",
             "get_voucher", "ledger_statement", "list_companies", "open_bills", "outstanding", "profit_and_loss", "profit_and_loss_layout",
             "search_ledgers", "trial_balance"],
            tools.Select(t => t.GetProperty("name").GetString()).Order(StringComparer.Ordinal));
        var create = tools.Single(t => t.GetProperty("name").GetString() == "create_voucher");
        var amount = create.GetProperty("inputSchema").GetProperty("properties").GetProperty("voucher").GetProperty("properties")
            .GetProperty("lines").GetProperty("items").GetProperty("properties").GetProperty("amount");
        Assert.Equal("string", amount.GetProperty("type").GetString());
        Assert.Contains("mcp.allowWrites", create.GetProperty("description").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_company_is_a_tool_error_not_a_crash()
    {
        var r = await Call(Client(), "trial_balance", new { company = 999, from = "2026-04-01", to = "2027-03-31" });
        Assert.True(r.GetProperty("isError").GetBoolean());
        Assert.DoesNotContain("An error occurred", Text(r), StringComparison.Ordinal);
    }
    [Fact]
    public async Task A_number_where_an_amount_string_belongs_names_the_field()
    {
        var r = await Call(Client(readWrite: true), "create_voucher", new
        {
            company = co,
            voucher = new
            {
                type = "receipt", date = "2026-05-10", narration = "x",
                lines = new object[] { new { ledgerId = b.Cash.Value, amount = 12345.6 }, new { ledgerId = b.Sales.Value, amount = "-12345.60" } },
            },
        });
        Assert.Equal("$.voucher.lines[0].amount must be a string like \"12345.60\".", Error(r));
    }

    [Fact]
    public async Task A_string_where_a_whole_number_belongs_names_the_field()
    {
        var r = await Call(Client(), "get_voucher", new { company = co, id = "seven" });
        Assert.Equal("$.id must be a whole number.", Error(r));
    }

    [Fact]
    public async Task A_list_where_an_object_belongs_names_the_field()
    {
        var r = await Call(Client(readWrite: true), "create_voucher", new { company = co, voucher = new[] { 1 } });
        Assert.Equal("$.voucher must be an object.", Error(r));
    }
    [Fact]
    public async Task Mcp_open_bills_and_allocating_create_voucher()
    {
        b.F.Set("mcp.allowWrites", "true");
        var sale = b.BillSale("1000.00");
        var bills = Ok(await Call(Client(name: "reader"), "open_bills", new { company = co, partyId = b.Customer.Value }));
        var bill = bills.GetProperty("rows")[0];
        Assert.Equal(("voucher", sale.Id, "1000.00"),
            (bill.GetProperty("source").GetString(), bill.GetProperty("id").GetInt64(), bill.GetProperty("open").GetProperty("amount").GetString()));
        Ok(await Call(Client(readWrite: true), "create_voucher", new
        {
            company = co,
            voucher = new
            {
                type = "receipt", date = "2026-06-10", narration = "",
                lines = new object[] { new { ledgerId = b.Cash.Value, amount = "600.00" }, new { ledgerId = b.Customer.Value, amount = "-600.00" } },
                allocations = new[] { new { partyId = b.Customer.Value, billSource = "voucher", billId = sale.Id, amount = "600.00" } },
            },
        }));
        Assert.Equal(Money.Parse("400.00"), b.Vouchers.OpenBills(b.Owner, co, b.Customer, null).Single().Open);
    }

    [Fact]
    public async Task Mcp_find_vouchers_matches_a_party_name()
    {
        var sale = b.BillSale("1000.00");
        b.Receipt("5.00");
        var found = Ok(await Call(Client(), "find_vouchers", new { company = co, from = "2026-04-01", to = "2027-03-31", text = "ravi" }));
        Assert.Equal(sale.Id, found.GetProperty("rows").EnumerateArray().Single().GetProperty("id").GetInt64());
    }
    [Fact]
    public async Task Mcp_find_vouchers_with_a_type_finds_older_vouchers_of_that_type()
    {
        await b.Vouchers.SaveInvoiceAsync(b.Owner, co, new InvoiceInput(VoucherType.Sale, new DateOnly(2026, 4, 5), "rent", b.Customer,
            TradeDirection.Outward, InvoiceKind.Regular, new StateCode("27"), false, null, null, null, null,
            [new InvoiceEntryLine(null, b.Sales, "Rod", "7214", new Quantity(1000), Money.Parse("10.00"), Money.Zero, new GstRate(0), new GstRate(0), false)]));
        for (var i = 0; i < 101; i++) b.Receipt("1.00", new DateOnly(2026, 5, 10), "rent");
        var found = Ok(await Call(Client(), "find_vouchers", new { company = co, from = "2026-04-01", to = "2027-03-31", type = "sale", text = "rent" }));
        Assert.Equal("sale", found.GetProperty("rows").EnumerateArray().Single().GetProperty("type").GetString());
    }
}
