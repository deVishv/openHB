using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Api;

/// <summary>The /api/v1 routes. Each one translates and calls one application service method; ApiGate has already authenticated.</summary>
public static class RestRoutes
{
    public static RouteGroupBuilder MapRestApi(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1");
        v1.MapGet("/info", (HttpContext c) =>
        {
            _ = c.Caller();
            return Results.Json(new { version = HostVersion.Current }, ApiJson.Rest);
        });
        return v1;
    }
}
