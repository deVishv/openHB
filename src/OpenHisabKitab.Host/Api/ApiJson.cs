using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenHisabKitab.Host.Api;

/// <summary>JSON options of the API port.</summary>
public static class ApiJson
{
    public static JsonSerializerOptions Rest { get; } = Strict(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applies the API's rules to a set of options: camelCase, enum names, and no unknown or missing fields.</summary>
    public static JsonSerializerOptions Strict(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        o.RespectNullableAnnotations = true;
        o.RespectRequiredConstructorParameters = true;
        o.NumberHandling = JsonNumberHandling.Strict;
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return o;
    }
}
