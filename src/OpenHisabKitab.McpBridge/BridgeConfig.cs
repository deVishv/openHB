using System.Text.Json;

namespace OpenHisabKitab.McpBridge;

/// <summary>
/// Where to forward and with which API token. CertificateFingerprint pins the host's certificate when the endpoint is
/// https, which it is once the Owner allows the API on the LAN.
/// </summary>
public sealed record BridgeConfig(Uri Endpoint, string Token, string? CertificateFingerprint)
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenHisabKitab", "mcp-bridge.json");

    sealed record File(string? Endpoint, string? Token, string? CertificateFingerprint);

    public static BridgeConfig Load(string path)
    {
        var f = JsonSerializer.Deserialize<File>(System.IO.File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The bridge config is empty.");
        if (!Uri.TryCreate(f.Endpoint, UriKind.Absolute, out var endpoint)) throw new InvalidDataException("Add endpoint to the bridge config.");
        return new BridgeConfig(endpoint, f.Token ?? "", string.IsNullOrWhiteSpace(f.CertificateFingerprint) ? null : f.CertificateFingerprint);
    }
}
