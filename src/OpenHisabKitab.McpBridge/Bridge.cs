using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenHisabKitab.McpBridge;

/// <summary>
/// Forwards MCP JSON-RPC from stdin to the host's /mcp, one message per line, and writes each reply as one line on stdout.
/// Only JSON-RPC goes to stdout; anything else goes to stderr. It holds no data and no rules (spec 4.1).
/// </summary>
public static class Bridge
{
    const int HostError = -32000;

    public static int CheckConfig(BridgeConfig config, TextWriter stderr)
    {
        string? problem = config.Endpoint.Scheme switch
        {
            not ("http" or "https") => "endpoint must be an http or https address.",
            _ when string.IsNullOrWhiteSpace(config.Token) => "Add token to the bridge config.",
            "https" when config.CertificateFingerprint is null => "Add certificateFingerprint to the bridge config. Find it in the host log.",
            "http" when !config.Endpoint.IsLoopback => "Use https for a host on another PC, so the token is not sent in plain text.",
            _ => null,
        };
        if (problem is null) return 0;
        stderr.WriteLine(problem);
        return 2;
    }

    public static async Task<int> RunAsync(
        TextReader stdin, TextWriter stdout, TextWriter stderr, HttpMessageHandler handler, BridgeConfig config, CancellationToken ct)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        while (await stdin.ReadLineAsync(ct) is { } line)
        {
            if (line.Trim().Length == 0) continue;
            try { await ForwardAsync(http, config, line, stdout, stderr, ct); }
            catch (HttpRequestException e) { Fail(line, $"The host could not be reached: {e.Message}", stdout, stderr); }
            await stdout.FlushAsync(ct);
        }
        return 0;
    }

    static async Task ForwardAsync(HttpClient http, BridgeConfig config, string line, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint) { Content = new StringContent(line, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            Fail(line, HostMessage(body) ?? $"The host answered {(int)response.StatusCode}.", stdout, stderr);
            return;
        }
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            foreach (var message in Events(body)) stdout.WriteLine(OneLine(message));
        }
        else if (body.Trim().Length > 0)
        {
            stdout.WriteLine(OneLine(body));
        }
    }

    // The data of each server-sent event. An event's data may span several data: lines, joined by newlines; a blank line ends it.
    static IEnumerable<string> Events(string body)
    {
        var data = new List<string>();
        foreach (var raw in body.Split('\n').Append(""))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line["data:".Length..].TrimStart(' '));
            else if (line.Length == 0 && data.Count > 0)
            {
                yield return string.Join('\n', data);
                data.Clear();
            }
        }
    }

    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // stdout carries one JSON-RPC message per line.
    static string OneLine(string json)
    {
        try { return JsonNode.Parse(json)?.ToJsonString(Compact) ?? json.Trim(); }
        catch (JsonException) { return json.ReplaceLineEndings(" ").Trim(); }
    }

    // A request gets a JSON-RPC error with its id; a notification has no id, so it gets only a line on stderr.
    static void Fail(string line, string message, TextWriter stdout, TextWriter stderr)
    {
        JsonNode? id = null;
        var isRequest = false;
        try
        {
            if (JsonNode.Parse(line) is JsonObject o && o.TryGetPropertyValue("id", out var found))
            {
                isRequest = true;
                id = found?.DeepClone();
            }
        }
        catch (JsonException) { isRequest = true; }

        if (!isRequest)
        {
            stderr.WriteLine(message);
            return;
        }
        var error = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = HostError, ["message"] = message },
        };
        stdout.WriteLine(error.ToJsonString(Compact));
    }

    static string? HostMessage(string body)
    {
        try
        {
            return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
