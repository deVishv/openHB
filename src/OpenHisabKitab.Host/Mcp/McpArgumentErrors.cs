using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenHisabKitab.Host.Mcp;

/// <summary>
/// The MCP SDK binds tool arguments before a tool runs, and a value of the wrong JSON type would otherwise reach the
/// assistant only as "An error occurred invoking". This call-tool filter turns that binding failure into the usual one
/// line message naming the field and what it must be, such as <c>$.voucher.lines[0].amount must be a string like "12345.60".</c>
/// </summary>
internal static partial class McpArgumentErrors
{
    // Each tool's declared parameters, found from the [McpServerTool] methods of the tool classes.
    static readonly Dictionary<string, ParameterInfo[]> Tools = typeof(ReadTools).GetMethods().Concat(typeof(WriteTools).GetMethods())
        .Select(m => (Method: m, Attr: m.GetCustomAttribute<McpServerToolAttribute>()))
        .Where(x => x.Attr?.Name is not null)
        .ToDictionary(x => x.Attr!.Name!, x => x.Method.GetParameters(), StringComparer.Ordinal);

    // Fields whose string form follows the wire rules of spec 4.1, with the example the other refusals show.
    static readonly Dictionary<string, string> Examples = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amount"] = "12345.60", ["unitPrice"] = "12345.60", ["discount"] = "12345.60", ["defaultPrice"] = "12345.60",
        ["quantity"] = "2.500", ["rate"] = "18.00", ["cessRate"] = "18.00",
        ["date"] = "2026-04-01", ["from"] = "2026-04-01", ["to"] = "2026-04-01", ["asAt"] = "2026-04-01",
    };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (ctx, ct) =>
        {
            try
            {
                return await next(ctx, ct);
            }
            catch (JsonException) when (Message(ctx.Params) is { } message)
            {
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = message }] };
            }
        };

    /// <summary>The refusal for the first argument that does not bind to its declared type, or null when none is found.</summary>
    internal static string? Message(CallToolRequestParams? call)
    {
        if (call?.Name is not { } name || !Tools.TryGetValue(name, out var parameters) || call.Arguments is not { } args) return null;
        foreach (var p in parameters)
        {
            if (p.Name is null || !args.TryGetValue(p.Name, out var value)) continue;
            try
            {
                JsonSerializer.Deserialize(value, p.ParameterType, McpJsonUtilities.DefaultOptions);
            }
            catch (JsonException e)
            {
                var path = "$." + p.Name + (e.Path is { Length: > 1 } inner ? inner[1..] : "");
                return $"{path} {Expected(TypeAt(p.ParameterType, e.Path), LastName(p.Name, e.Path))}";
            }
        }
        return null;
    }

    static string Expected(Type? type, string field)
    {
        var t = type is null ? null : Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return Examples.TryGetValue(field, out var ex) ? $"must be a string like \"{ex}\"." : "must be a string.";
        if (t == typeof(int) || t == typeof(long) || t == typeof(short)) return "must be a whole number.";
        if (t == typeof(bool)) return "must be true or false.";
        if (t is not null && t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t)) return "must be a list.";
        if (t is not null && t.IsClass) return "must be an object.";
        return "has the wrong type.";
    }

    // The declared type at a path such as $.lines[0].amount inside a parameter of the given type.
    static Type? TypeAt(Type root, string? path)
    {
        var type = root;
        foreach (Match m in PathPart().Matches(path ?? ""))
        {
            if (m.Groups["index"].Success) type = ElementType(type);
            else type = type?.GetProperty(m.Groups["name"].Value, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType;
            if (type is null) return null;
        }
        return type;
    }

    static Type? ElementType(Type? t) =>
        t is null ? null
        : t.IsArray ? t.GetElementType()
        : t.GetInterfaces().Append(t).FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];

    static string LastName(string parameter, string? path) =>
        PathPart().Matches(path ?? "").Select(m => m.Groups["name"]).LastOrDefault(g => g.Success)?.Value ?? parameter;

    [GeneratedRegex(@"\.(?<name>[A-Za-z_][A-Za-z0-9_]*)|\[(?<index>\d+)\]")]
    private static partial Regex PathPart();
}
