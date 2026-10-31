using System.Text.Json;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>
/// Certificate fingerprints this PC has accepted, one per host and port (spec 4, trust on first use). A file that cannot be
/// read counts as empty, so the user is asked again rather than trusting anything.
/// </summary>
public sealed class KnownHosts
{
    readonly string path;
    readonly Dictionary<string, string> pins;

    public KnownHosts(string path)
    {
        this.path = path;
        pins = Load(path);
    }

    public string? Fingerprint(HostAddress host) => pins.GetValueOrDefault(Key(host));

    public void Trust(HostAddress host, string fingerprint)
    {
        pins[Key(host)] = fingerprint;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(pins, new JsonSerializerOptions { WriteIndented = true }));
    }

    static string Key(HostAddress host) => host.ToString().ToLowerInvariant();

    static Dictionary<string, string> Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }
}
