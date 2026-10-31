using OpenHisabKitab.Contracts;

namespace OpenHisabKitab.Host.Grpc;

/// <summary>The host's version. Clients must run exactly this version (spec 13, Version match).</summary>
public static class HostVersion
{
    public static string Current => ProductVersion.Current;
}
