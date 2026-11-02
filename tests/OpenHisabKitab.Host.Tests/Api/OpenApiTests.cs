using System.Net;
using System.Text.Json;
using OpenHisabKitab.Host.Grpc;
using OpenHisabKitab.Host.Tests.Grpc;

namespace OpenHisabKitab.Host.Tests.Api;

public sealed class OpenApiTests : IDisposable
{
    readonly HostFactory f = new();

    public OpenApiTests() => f.EnableApi();

    public void Dispose() => f.Dispose();

    async Task<JsonElement> Document() => ApiBooks.Json(await f.ApiClient(f.NewToken()).GetStringAsync("/api/v1/openapi.json"));

    static JsonElement Resolve(JsonElement doc, JsonElement schema)
    {
        while (schema.TryGetProperty("$ref", out var r))
        {
            var name = r.GetString()!.Split('/')[^1];
            schema = doc.GetProperty("components").GetProperty("schemas").GetProperty(name);
        }
        return schema;
    }

    static JsonElement Property(JsonElement doc, string schema, string property) =>
        Resolve(doc, doc.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("properties").GetProperty(property));

    [Fact]
    public async Task Openapi_document_needs_a_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.ApiClient(null).GetAsync("/api/v1/openapi.json")).StatusCode);
    }

    [Fact]
    public async Task Openapi_lists_every_rest_route()
    {
        var paths = (await Document()).GetProperty("paths").EnumerateObject().Select(p => p.Name).ToHashSet();
        string[] expected =
        [
            "/api/v1/info", "/api/v1/companies", "/api/v1/companies/{company}", "/api/v1/companies/{company}/years",
            "/api/v1/companies/{company}/groups", "/api/v1/companies/{company}/ledgers", "/api/v1/companies/{company}/ledgers/{id}/name",
            "/api/v1/companies/{company}/parties", "/api/v1/companies/{company}/parties/{id}", "/api/v1/companies/{company}/items",
            "/api/v1/companies/{company}/items/{id}", "/api/v1/companies/{company}/vouchers", "/api/v1/companies/{company}/vouchers/{id}",
            "/api/v1/companies/{company}/vouchers/journal", "/api/v1/companies/{company}/vouchers/invoice",
            "/api/v1/companies/{company}/vouchers/{id}/journal", "/api/v1/companies/{company}/vouchers/{id}/invoice",
            "/api/v1/companies/{company}/vouchers/{id}/cancel", "/api/v1/companies/{company}/reports/trial-balance",
            "/api/v1/companies/{company}/reports/profit-and-loss", "/api/v1/companies/{company}/reports/balance-sheet",
            "/api/v1/companies/{company}/reports/ledger-statement",
            "/api/v1/companies/{company}/reports/balance-sheet/layout", "/api/v1/companies/{company}/reports/profit-and-loss/layout",
            "/api/v1/companies/{company}/reports/cash-flow", "/api/v1/companies/{company}/reports/outstanding",
            "/api/v1/companies/{company}/parties/{id}/open-bills", "/api/v1/companies/{company}/parties/{id}/opening-bills",
        ];
        Assert.Equal([], expected.Except(paths));
    }

    [Fact]
    public async Task Money_fields_are_strings_in_the_schema()
    {
        var doc = await Document();
        var total = Property(doc, "TrialBalanceDto", "totalDebit");
        Assert.Equal("string", total.GetProperty("type").GetString());
        Assert.Equal(@"^-?(0|[1-9][0-9]*)\.[0-9]{2}$", total.GetProperty("pattern").GetString());
        var quantity = Property(doc, "InvoiceLineDto", "quantity");
        Assert.Equal(@"^-?(0|[1-9][0-9]*)\.[0-9]{3}$", quantity.GetProperty("pattern").GetString());
        var rate = Property(doc, "InvoiceLineDto", "rate");
        Assert.Equal(@"^(0|[1-9][0-9]*)\.[0-9]{2}$", rate.GetProperty("pattern").GetString());
        Assert.Equal("string", Property(doc, "LineRequest", "amount").GetProperty("type").GetString());
        Assert.DoesNotContain("paise", doc.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Openapi_version_is_the_host_version_and_it_declares_bearer_tokens()
    {
        var doc = await Document();
        Assert.Equal(HostVersion.Current, doc.GetProperty("info").GetProperty("version").GetString());
        Assert.Equal("bearer", doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer").GetProperty("scheme").GetString());
    }
}
