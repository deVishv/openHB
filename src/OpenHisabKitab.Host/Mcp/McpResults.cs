using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using OpenHisabKitab.Host.Api;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Mcp;

/// <summary>A list cut to RowLimit rows. Omitted says how many more there were; Note says what to do about it.</summary>
public sealed record McpList<T>(IReadOnlyList<T> Rows, int Omitted, string? Note);

/// <summary>
/// Tool results built here, not by the MCP SDK: the SDK would write Money as {"paise":...} and would hide every refusal
/// behind a generic message. Each result is one text block of JSON written with ApiJson.Mcp.
/// </summary>
internal static class McpResults
{
    public const int RowLimit = 100;

    public static CallToolResult Ok(object value) =>
        new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value, value.GetType(), ApiJson.Mcp) }] };

    static CallToolResult Refused(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    public static async Task<CallToolResult> Run(ILogger log, string tool, Func<Task<object>> work)
    {
        try { return Ok(await work()); }
        catch (ServiceException e) { return Refused(e.Message); }
        catch (Exception e)
        {
            log.LogError(e, "MCP tool {Tool} failed.", tool);
            return Refused(ApiErrors.InternalMessage);
        }
    }

    public static Task<CallToolResult> Run(ILogger log, string tool, Func<object> work) => Run(log, tool, () => Task.FromResult(work()));

    public static McpList<T> Capped<T>(IReadOnlyList<T> rows)
    {
        var omitted = Math.Max(0, rows.Count - RowLimit);
        var note = omitted > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{omitted} more rows. Narrow the dates or the search.")
            : null;
        return new McpList<T>([.. rows.Take(RowLimit)], omitted, note);
    }
}
