using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>A host setting as the settings screen lists it (spec 7): its type, default, one line description, and allowed values.</summary>
public sealed record HostSettingDefinition(string Key, string Type, string DefaultJson, string Description, string AllowedValues);

/// <summary>The local REST API and MCP settings of spec 4.1, read from host.db. A missing or unreadable value means the default.</summary>
public sealed record ApiSettings(
    bool Enabled, int Port, bool AllowLan, IReadOnlyList<string> AllowedOrigins, bool McpEnabled, bool McpAllowWrites)
{
    public static ApiSettings Read(SqliteConnection hostDb) => new(
        Value(hostDb, HostSettings.ApiEnabled, false),
        Value(hostDb, HostSettings.ApiPort, HostSettings.DefaultApiPort),
        Value(hostDb, HostSettings.ApiAllowLan, false),
        Value<string[]>(hostDb, HostSettings.ApiAllowedOrigins, []),
        Value(hostDb, HostSettings.McpEnabled, false),
        Value(hostDb, HostSettings.McpAllowWrites, false));

    static T Value<T>(SqliteConnection c, string key, T fallback)
    {
        if (HostSettingsRepository.Get(c, key) is not { } json) return fallback;
        try { return JsonSerializer.Deserialize<T>(json) ?? fallback; }
        catch (JsonException) { return fallback; }
    }
}

public static class HostSettings
{
    public const int DefaultApiPort = 47251;

    public const string ApiEnabled = "api.enabled";
    public const string ApiPort = "api.port";
    public const string ApiAllowLan = "api.allowLan";
    public const string ApiAllowedOrigins = "api.allowedOrigins";
    public const string McpEnabled = "mcp.enabled";
    public const string McpAllowWrites = "mcp.allowWrites";

    public static IReadOnlyList<HostSettingDefinition> All { get; } =
    [
        new(ApiEnabled, "bool", "false", "Turns on the local REST API port.", "true, false"),
        new(ApiPort, "int", "47251", "Port for the REST API and MCP.", "1024 to 65535, not the gRPC port"),
        new(ApiAllowLan, "bool", "false", "Also listen on the LAN, with TLS.", "true, false"),
        new(ApiAllowedOrigins, "string[]", "[]", "Web origins allowed to call the API.", "origins such as https://example.com"),
        new(McpEnabled, "bool", "false", "Turns on the MCP endpoint. Needs api.enabled.", "true, false"),
        new(McpAllowWrites, "bool", "false", "Lets MCP create vouchers and parties.", "true, false"),
    ];
}
