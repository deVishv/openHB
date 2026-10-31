using System.Globalization;
using OpenHisabKitab.Client.Model.Resources;

namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>A host typed as a name or IP address and a port (spec 13, Remote ready client).</summary>
public sealed record HostAddress(string Name, int Port)
{
    public static bool TryParse(string name, string port, out HostAddress address, out string? error)
    {
        address = null!;
        var n = name.Trim();
        if (n.Length == 0 || Uri.CheckHostName(n) == UriHostNameType.Unknown)
        {
            error = Text.EnterHostName;
            return false;
        }
        if (!int.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p is < 1 or > 65535)
        {
            error = Text.EnterPort;
            return false;
        }
        error = null;
        address = new HostAddress(n, p);
        return true;
    }

    /// <summary>The name as it goes in a URI: an IPv6 address in brackets.</summary>
    public string UriHost => Name.Contains(':', StringComparison.Ordinal) ? $"[{Name}]" : Name;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{UriHost}:{Port}");
}
