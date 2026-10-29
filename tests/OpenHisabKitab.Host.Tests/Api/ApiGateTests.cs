using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Host.Grpc;
using OpenHisabKitab.Host.Tests.Grpc;

namespace OpenHisabKitab.Host.Tests.Api;

public sealed class ApiGateTests : IDisposable
{
    readonly HostFactory f = new();

    public void Dispose() => f.Dispose();

    static async Task<(HttpStatusCode Status, string Code, string Message)> Error(HttpResponseMessage r)
    {
        var e = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error");
        Assert.Equal("application/json", r.Content.Headers.ContentType?.MediaType);
        return (r.StatusCode, e.GetProperty("code").GetString()!, e.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Info_needs_a_token_and_reports_the_host_version()
    {
        f.EnableApi();
        var anon = await f.ApiClient(null).GetAsync("/api/v1/info");
        Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);
        Assert.Equal("Bearer", anon.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal("""{"error":{"code":"not_authenticated","message":"Send a valid API token."}}""", await anon.Content.ReadAsStringAsync());
        var ok = await f.ApiClient(f.NewToken()).GetFromJsonAsync<JsonElement>("/api/v1/info");
        Assert.Equal(HostVersion.Current, ok.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Api_off_by_default_answers_503()
    {
        var token = f.NewToken();
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "unavailable", "The API is off. The Owner can turn on api.enabled."),
            await Error(await f.ApiClient(token).GetAsync("/api/v1/info")));
    }

    [Fact]
    public async Task Turning_the_api_off_applies_at_once()
    {
        f.EnableApi();
        var api = f.ApiClient(f.NewToken());
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/info")).StatusCode);
        f.Set("api.enabled", "false");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await api.GetAsync("/api/v1/info")).StatusCode);
    }

    [Fact]
    public async Task Session_token_is_refused_on_the_api_port()
    {
        f.EnableApi();
        var session = f.Owner().Token;
        Assert.Equal((HttpStatusCode.Unauthorized, "not_authenticated", "Send a valid API token."),
            await Error(await f.ApiClient(session).GetAsync("/api/v1/info")));
    }

    [Fact]
    public async Task Origin_is_refused_before_the_token_is_checked()
    {
        f.EnableApi();
        var bad = f.ApiClient("ohk_bad");
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/info");
            request.Headers.Add("Origin", "https://evil.example");
            Assert.Equal((HttpStatusCode.Forbidden, "permission_denied", "Requests from web pages are not allowed."),
                await Error(await bad.SendAsync(request)));
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync("/api/v1/info")).StatusCode);
    }

    [Fact]
    public async Task Origin_with_a_good_token_is_still_refused()
    {
        f.EnableApi();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/info");
        request.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await f.ApiClient(f.NewToken()).SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Allowed_origin_passes()
    {
        f.EnableApi();
        f.Set("api.allowedOrigins", """["https://ok.example"]""");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/info");
        request.Headers.Add("Origin", "https://OK.example/");
        Assert.Equal(HttpStatusCode.OK, (await f.ApiClient(f.NewToken()).SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Lan_caller_is_refused_unless_allowLan()
    {
        using var lan = new HostFactory(callerIsLoopback: false);
        lan.EnableApi();
        var api = lan.ApiClient(lan.NewToken());
        Assert.Equal((HttpStatusCode.Forbidden, "permission_denied", "The API accepts only programs on the host PC."),
            await Error(await api.GetAsync("/api/v1/info")));
        lan.Set("api.allowLan", "true");
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/info")).StatusCode);
        lan.Set("api.allowLan", "false");
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/v1/info")).StatusCode);
    }

    [Fact]
    public async Task Api_routes_are_404_on_the_grpc_port()
    {
        f.EnableApi(mcp: true);
        var token = f.NewToken();
        var grpcPort = f.CreateClient();
        grpcPort.DefaultRequestHeaders.Authorization = new("Bearer", token);
        foreach (var path in new[] { "/api/v1/info", "/API/v1/info", "/api/v1/openapi.json" })
            Assert.Equal((HttpStatusCode.NotFound, "not_found", "No such route."), await Error(await grpcPort.GetAsync(path)));
        var init = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", Encoding.UTF8, "application/json");
        Assert.Equal((HttpStatusCode.NotFound, "not_found", "No such route."), await Error(await grpcPort.PostAsync("/MCP", init)));
    }

    [Fact]
    public async Task Grpc_is_not_served_on_the_api_port()
    {
        f.EnableApi();
        var api = f.ApiClient(f.NewToken());
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ohk.v1.Session/GetInfo") { Content = new ByteArrayContent([0, 0, 0, 0, 0]) };
        request.Content.Headers.ContentType = new("application/grpc");
        Assert.Equal((HttpStatusCode.NotFound, "not_found", "No such route."), await Error(await api.SendAsync(request)));
    }

    [Fact]
    public void Grpc_still_works_beside_the_api()
    {
        f.EnableApi();
        var info = new Contracts.V1.Session.SessionClient(f.Channel()).GetInfo(new Contracts.V1.Empty());
        Assert.Equal(HostVersion.Current, info.Version);
    }

    [Fact]
    public async Task Unknown_route_and_wrong_method_use_the_error_shape()
    {
        f.EnableApi();
        var api = f.ApiClient(f.NewToken());
        Assert.Equal((HttpStatusCode.NotFound, "not_found", "No such route."), await Error(await api.GetAsync("/api/v1/nothing")));
        Assert.Equal((HttpStatusCode.NotFound, "not_found", "No such route."), await Error(await api.GetAsync("/elsewhere")));
        Assert.Equal((HttpStatusCode.MethodNotAllowed, "method_not_allowed", "This route does not accept DELETE."),
            await Error(await api.DeleteAsync("/api/v1/info")));
    }

    [Fact]
    public async Task Throttled_address_gets_429_with_retry_after()
    {
        f.EnableApi();
        var bad = f.ApiClient("ohk_bad");
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await bad.GetAsync("/api/v1/info")).StatusCode);
        var r = await bad.GetAsync("/api/v1/info");
        Assert.Equal(TimeSpan.FromSeconds(30), r.Headers.RetryAfter?.Delta);
        Assert.Equal((HttpStatusCode.TooManyRequests, "too_many_attempts", "Too many attempts. Try again in 30 seconds."), await Error(r));
        Assert.Equal(HttpStatusCode.OK, (await f.ApiClient(f.NewToken()).GetAsync("/api/v1/info")).StatusCode);
    }

    [Fact]
    public async Task Mcp_off_answers_503()
    {
        f.EnableApi();
        var init = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", Encoding.UTF8, "application/json");
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "unavailable", "MCP is off. The Owner can turn on mcp.enabled."),
            await Error(await f.ApiClient(f.NewToken()).PostAsync("/mcp", init)));
    }

    [Fact]
    public async Task Doubled_slashes_do_not_reach_the_api_on_the_grpc_port()
    {
        f.EnableApi(mcp: true);
        var grpcPort = f.CreateClient();
        grpcPort.DefaultRequestHeaders.Authorization = new("Bearer", f.NewToken());
        foreach (var path in new[] { "//api/v1/info", "/./api/v1/info", "//api/v1/openapi.json" })
            Assert.Equal(HttpStatusCode.NotFound, (await grpcPort.GetAsync(path)).StatusCode);
        var init = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""", Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "//mcp") { Content = init };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        Assert.Equal(HttpStatusCode.NotFound, (await grpcPort.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Allowed_origin_gets_cors_preflight_and_headers()
    {
        f.EnableApi();
        f.Set("api.allowedOrigins", """["https://ok.example"]""");
        var anon = f.ApiClient(null);
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/info");
        preflight.Headers.Add("Origin", "https://ok.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        var r = await anon.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("https://ok.example", r.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("authorization", r.Headers.GetValues("Access-Control-Allow-Headers").Single(), StringComparison.OrdinalIgnoreCase);

        using var call = new HttpRequestMessage(HttpMethod.Get, "/api/v1/info");
        call.Headers.Add("Origin", "https://ok.example");
        var ok = await f.ApiClient(f.NewToken()).SendAsync(call);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("https://ok.example", ok.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var evil = new HttpRequestMessage(HttpMethod.Options, "/api/v1/info");
        evil.Headers.Add("Origin", "https://evil.example");
        evil.Headers.Add("Access-Control-Request-Method", "GET");
        var refused = await anon.SendAsync(evil);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.False(refused.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
