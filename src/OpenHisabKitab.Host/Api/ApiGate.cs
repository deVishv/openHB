using System.Text.Json;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Api;

/// <summary>
/// Runs first on every request (spec 4.1). It keeps the REST and MCP routes on the API port and gRPC off it, applies
/// api.enabled, the LAN rule, the Origin check, and mcp.enabled, and turns the bearer API token into the caller's
/// context. Errors from anything behind it leave in the one error shape.
/// </summary>
internal sealed class ApiGate(
    RequestDelegate next, IListenerAccessor listener, IRemoteAddressAccessor remote, HostData data, ApiTokenService tokens,
    ILogger<ApiGate> log)
{
    const string NoRoute = "No such route.";

    public async Task InvokeAsync(HttpContext c)
    {
        // Routing ignores case, so these checks must too, or /API/v1 would slip past them.
        var path = c.Request.Path;
        var isMcp = path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);
        var isApi = path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        if (!listener.OnApiPort(c))
        {
            if (isApi || isMcp) await ApiErrors.Write(c, 404, "not_found", NoRoute);
            else await next(c);
            return;
        }
        if (!isMcp && !path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            await ApiErrors.Write(c, 404, "not_found", NoRoute);
            return;
        }

        try
        {
            ApiSettings settings;
            using (var db = data.OpenHost()) settings = ApiSettings.Read(db);
            if (!settings.Enabled)
                throw new ServiceException(ServiceError.Unavailable, "The API is off. The Owner can turn on api.enabled.");
            var caller = remote.RemoteAddress(c);
            if (!settings.AllowLan && !Loopback.Is(caller))
                throw new ServiceException(ServiceError.PermissionDenied, "The API accepts only programs on the host PC.");
            // Before the token is read, so a web page never reaches the token check or the failed token count.
            if (c.Request.Headers.Origin.Count > 0)
            {
                var origin = c.Request.Headers.Origin.ToString();
                if (!Allowed(origin, settings.AllowedOrigins))
                    throw new ServiceException(ServiceError.PermissionDenied, "Requests from web pages are not allowed.");
                // An allowed web origin must be told so, or the browser hides every answer from the page.
                c.Response.OnStarting(() =>
                {
                    c.Response.Headers.AccessControlAllowOrigin = origin;
                    c.Response.Headers.Vary = "Origin";
                    return Task.CompletedTask;
                });
                if (HttpMethods.IsOptions(c.Request.Method) && c.Request.Headers.AccessControlRequestMethod.Count > 0)
                {
                    // A browser's preflight carries no token; it only asks what the real request may send.
                    c.Response.Headers.AccessControlAllowMethods = "GET, POST, PUT, DELETE";
                    c.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Accept, Mcp-Protocol-Version";
                    c.Response.Headers.AccessControlMaxAge = "600";
                    c.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }
            }
            if (isMcp && !settings.McpEnabled)
                throw new ServiceException(ServiceError.Unavailable, "MCP is off. The Owner can turn on mcp.enabled.");

            c.Items[ApiCall.ContextKey] = tokens.Authenticate(Bearer(c), isMcp ? ChangeSource.Mcp : ChangeSource.Rest, caller);
            await next(c);
            if (!c.Response.HasStarted && c.Response.StatusCode is 404 or 405 && c.Response.ContentType is null)
                await NoEndpoint(c);
        }
        catch (Exception e) when (!c.Response.HasStarted)
        {
            await Fail(c, e);
        }
    }

    // A 405 comes from routing matching the path but not the method; a 404 from no endpoint at all.
    static Task NoEndpoint(HttpContext c) =>
        c.Response.StatusCode == 405
            ? ApiErrors.Write(c, 405, "method_not_allowed", $"This route does not accept {c.Request.Method}.")
            : ApiErrors.Write(c, 404, "not_found", NoRoute);

    Task Fail(HttpContext c, Exception e)
    {
        c.Response.Clear();
        switch (e)
        {
            case ServiceException se:
                return ApiErrors.Write(se, c);
            case BadHttpRequestException { InnerException: JsonException je }:
                return BadJson(c, je);
            case JsonException je:
                return BadJson(c, je);
            case BadHttpRequestException bad:
                return ApiErrors.Write(c, bad.StatusCode, "invalid", "The request is not valid.");
            case OperationCanceledException when c.RequestAborted.IsCancellationRequested:
                return Task.CompletedTask;
            default:
                log.LogError(e, "API call {Method} {Path} failed.", c.Request.Method, c.Request.Path);
                return ApiErrors.Write(c, 500, "internal", ApiErrors.InternalMessage);
        }
    }

    // A JsonReaderException (internal to System.Text.Json) inside means the text is not JSON at all. A path of $ alone means a required field is missing.
    static Task BadJson(HttpContext c, JsonException e) =>
        ApiErrors.Write(c, 400, "invalid",
            e.InnerException?.GetType().Name == "JsonReaderException" || e.Path is null ? "The request body is not valid JSON."
            : e.Path == "$" ? "Add the missing fields to the request body."
            : $"Check {e.Path} in the request body.");

    static bool Allowed(string origin, IReadOnlyList<string> allowed) =>
        allowed.Any(a => string.Equals(a.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    static string? Bearer(HttpContext c)
    {
        const string prefix = "Bearer ";
        var header = c.Request.Headers.Authorization.ToString();
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? header[prefix.Length..].Trim() : null;
    }
}

/// <summary>The caller of an API port request, as the gate resolved it.</summary>
internal static class ApiCall
{
    public const string ContextKey = "ohk.context";

    public static CallContext Caller(this HttpContext c) =>
        c.Items.TryGetValue(ContextKey, out var v) && v is CallContext ctx
            ? ctx
            : throw new ServiceException(ServiceError.NotAuthenticated, "Send a valid API token.");
}
