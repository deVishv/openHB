using System.Text.Json;

namespace OpenHisabKitab.McpBridge.Tests;

public sealed class BridgeTests : IDisposable
{
    readonly BridgeHost host = new();

    public void Dispose() => host.Dispose();

    async Task<(int Exit, string[] Out, string Err)> Run(string token, params string[] lines)
    {
        var config = new BridgeConfig(new Uri("http://localhost/mcp"), token, null);
        var stdin = new StringReader(string.Join("\n", lines) + "\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await Bridge.RunAsync(stdin, stdout, stderr, host.Server.CreateHandler(), config, CancellationToken.None);
        var output = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        return (exit, output, stderr.ToString());
    }

    const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";
    const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    const string ToolsList = """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""";
    const string ListCompanies = """{"jsonrpc":"2.0","id":"three","method":"tools/call","params":{"name":"list_companies","arguments":{}}}""";

    [Fact]
    public async Task Initialize_list_and_call_round_trip()
    {
        var (exit, lines, _) = await Run(host.OwnerToken(), Initialize, Initialized, ToolsList, ListCompanies);
        Assert.Equal(0, exit);
        Assert.Equal(3, lines.Length);
        var replies = lines.Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal("2.0", replies[0].GetProperty("jsonrpc").GetString());
        Assert.Equal(1, replies[0].GetProperty("id").GetInt32());
        Assert.Equal("Open Hisab Kitab", replies[0].GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Equal(2, replies[1].GetProperty("id").GetInt32());
        Assert.True(replies[1].GetProperty("result").GetProperty("tools").GetArrayLength() > 0);
        Assert.Equal("three", replies[2].GetProperty("id").GetString());
        Assert.False(replies[2].GetProperty("result").TryGetProperty("isError", out var isError) && isError.GetBoolean());
    }

    [Fact]
    public async Task Notification_writes_nothing()
    {
        var (_, lines, err) = await Run(host.OwnerToken(), Initialized);
        Assert.Empty(lines);
        Assert.Equal("", err);
    }

    [Fact]
    public async Task Bad_token_becomes_a_json_rpc_error_with_the_host_message()
    {
        host.OwnerToken();
        var (exit, lines, _) = await Run("ohk_bad", ToolsList);
        Assert.Equal(0, exit);
        var reply = JsonDocument.Parse(Assert.Single(lines)).RootElement;
        Assert.Equal(2, reply.GetProperty("id").GetInt32());
        Assert.Equal(-32000, reply.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("Send a valid API token.", reply.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Failed_notification_writes_nothing_to_stdout()
    {
        host.OwnerToken();
        var (_, lines, err) = await Run("ohk_bad", Initialized);
        Assert.Empty(lines);
        Assert.Contains("Send a valid API token.", err, StringComparison.Ordinal);
    }

    [Fact]
    public void Https_without_fingerprint_refuses_to_start()
    {
        var stderr = new StringWriter();
        Assert.Equal(2, Bridge.CheckConfig(new BridgeConfig(new Uri("https://192.168.1.5:47251/mcp"), "ohk_x", null), stderr));
        Assert.Equal("Add certificateFingerprint to the bridge config. Find it in the host log.", stderr.ToString().Trim());
        Assert.Equal(0, Bridge.CheckConfig(new BridgeConfig(new Uri("https://192.168.1.5:47251/mcp"), "ohk_x", "AA:BB"), new StringWriter()));
        Assert.Equal(0, Bridge.CheckConfig(new BridgeConfig(new Uri("http://127.0.0.1:47251/mcp"), "ohk_x", null), new StringWriter()));
    }

    [Fact]
    public void Config_loads_from_json()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, """{"endpoint":"http://127.0.0.1:47251/mcp","token":"ohk_abc","certificateFingerprint":null}""");
        try
        {
            Assert.Equal(new BridgeConfig(new Uri("http://127.0.0.1:47251/mcp"), "ohk_abc", null), BridgeConfig.Load(path));
        }
        finally { File.Delete(path); }
    }
}
