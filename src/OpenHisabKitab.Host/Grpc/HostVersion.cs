using System.Reflection;

namespace OpenHisabKitab.Host.Grpc;

/// <summary>The host's version. Clients must run exactly this version (spec 13, Version match).</summary>
public static class HostVersion
{
    public static string Current { get; } = Read();

    static string Read()
    {
        var info = typeof(HostVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? info : info[..plus];
    }
}
