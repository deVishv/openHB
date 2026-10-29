using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Api;

/// <summary>The one error shape of the API port: {"error":{"code":"...","message":"...","current":...}}.</summary>
public static class ApiErrors
{
    public const string InternalMessage = "Something went wrong. Details are in the host log.";

    static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static (int Status, string Code) Map(ServiceError e) => e switch
    {
        ServiceError.NotAuthenticated => (401, "not_authenticated"),
        ServiceError.TooManyAttempts => (429, "too_many_attempts"),
        ServiceError.PermissionDenied => (403, "permission_denied"),
        ServiceError.NotFound => (404, "not_found"),
        ServiceError.Conflict => (409, "conflict"),
        ServiceError.Invalid => (400, "invalid"),
        ServiceError.Locked => (422, "locked"),
        ServiceError.OpeningDifference => (422, "opening_difference"),
        ServiceError.SetupRequired => (422, "setup_required"),
        ServiceError.TooNew => (422, "too_new"),
        ServiceError.Unavailable => (503, "unavailable"),
        _ => (500, "internal"),
    };

    public static Task Write(ServiceException e, HttpContext c)
    {
        var (status, code) = Map(e.Error);
        if (e.Error == ServiceError.NotAuthenticated) c.Response.Headers.WWWAuthenticate = "Bearer";
        if (e.RetryAfter is { } wait)
            c.Response.Headers.RetryAfter = ((long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return Write(c, status, code, e.Message, e.Error == ServiceError.Conflict ? e.Current : null);
    }

    public static async Task Write(HttpContext c, int status, string code, string message, object? current = null)
    {
        c.Response.StatusCode = status;
        c.Response.ContentType = "application/json";
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteStartObject("error");
            w.WriteString("code", code);
            w.WriteString("message", message);
            if (current is not null)
            {
                w.WritePropertyName("current");
                JsonSerializer.Serialize(w, current, current.GetType(), ApiJson.Rest);
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        await c.Response.Body.WriteAsync(buffer.ToArray());
    }
}
