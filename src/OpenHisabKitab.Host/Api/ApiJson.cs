using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Host.Api;

/// <summary>
/// JSON options of the API port. Money, quantities, and rates are JSON strings in the wire form of spec 4.1, never JSON
/// numbers. MCP results also carry each amount as the screens show it, so the assistant quotes the same figures.
/// </summary>
public static class ApiJson
{
    public static JsonSerializerOptions Rest { get; } = Strict(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Mcp { get; } = Strict(new JsonSerializerOptions(JsonSerializerDefaults.Web), moneyWithText: true);

    /// <summary>Applies the API's rules to a set of options: camelCase, enum names, no unknown or missing fields, wire amounts.</summary>
    public static JsonSerializerOptions Strict(JsonSerializerOptions o, bool moneyWithText = false)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        o.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        o.RespectNullableAnnotations = true;
        o.RespectRequiredConstructorParameters = true;
        o.NumberHandling = JsonNumberHandling.Strict;
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        o.Converters.Add(moneyWithText ? new MoneyWithText() : new MoneyWire());
        o.Converters.Add(new QuantityWire());
        o.Converters.Add(new RateWire());
        return o;
    }

    static string WireString(ref Utf8JsonReader r, string example) =>
        r.TokenType == JsonTokenType.String ? r.GetString()! : throw new JsonException($"Expected a string like \"{example}\".");

    sealed class MoneyWire : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            WireAmount.TryParseMoney(WireString(ref r, "12345.60"), out var m) ? m : throw new JsonException("Expected a string like \"12345.60\".");

        public override void Write(Utf8JsonWriter w, Money v, JsonSerializerOptions o) => w.WriteStringValue(WireAmount.Format(v));
    }

    /// <summary>{"amount":"1234567.80","text":"12,34,567.80"}. Written only; MCP tools take amounts as strings.</summary>
    sealed class MoneyWithText : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            throw new JsonException("Send amounts as a string like \"12345.60\".");

        public override void Write(Utf8JsonWriter w, Money v, JsonSerializerOptions o)
        {
            w.WriteStartObject();
            w.WriteString("amount", WireAmount.Format(v));
            w.WriteString("text", IndianNumberFormat.Format(v));
            w.WriteEndObject();
        }
    }

    sealed class QuantityWire : JsonConverter<Quantity>
    {
        public override Quantity Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            WireAmount.TryParseQuantity(WireString(ref r, "2.500"), out var q) ? q : throw new JsonException("Expected a string like \"2.500\".");

        public override void Write(Utf8JsonWriter w, Quantity v, JsonSerializerOptions o) => w.WriteStringValue(WireAmount.Format(v));
    }

    sealed class RateWire : JsonConverter<GstRate>
    {
        public override GstRate Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            WireAmount.TryParseRate(WireString(ref r, "18.00"), out var g) ? g : throw new JsonException("Expected a string like \"18.00\".");

        public override void Write(Utf8JsonWriter w, GstRate v, JsonSerializerOptions o) => w.WriteStringValue(WireAmount.Format(v));
    }
}
