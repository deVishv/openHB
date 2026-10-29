using System.Globalization;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OpenHisabKitab.McpBridge;

/// <summary>ohk-mcp-bridge [--config path]. Reads the config, checks it, and forwards stdin to the host until stdin ends.</summary>
public static class BridgeProgram
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var path = args is ["--config", var p] ? p : BridgeConfig.DefaultPath;

        BridgeConfig config;
        try { config = BridgeConfig.Load(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            await Console.Error.WriteLineAsync($"Cannot read the bridge config at {path}: {e.Message}");
            return 2;
        }
        if (Bridge.CheckConfig(config, Console.Error) is not 0 and var code) return code;

        using var handler = new SocketsHttpHandler();
        if (config.CertificateFingerprint is { } pinned)
            // The host's certificate is self signed, so it is trusted by its fingerprint, never by turning checks off.
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) =>
                cert is not null && string.Equals(Fingerprint(cert), pinned.Trim(), StringComparison.OrdinalIgnoreCase);

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        try { return await Bridge.RunAsync(Console.In, Console.Out, Console.Error, handler, config, stop.Token); }
        catch (OperationCanceledException) { return 0; }
    }

    // The same fingerprint the host logs and writes to host-cert-fingerprint.txt (Host/Security/CertificateStore.cs).
    static string Fingerprint(X509Certificate cert) =>
        string.Join(':', SHA256.HashData(cert.GetRawCertData()).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
}
