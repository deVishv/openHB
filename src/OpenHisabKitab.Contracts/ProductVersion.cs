using System.Reflection;

namespace OpenHisabKitab.Contracts;

/// <summary>The one product version, shared by client and host so they cannot drift (spec 13, Version match).</summary>
public static class ProductVersion
{
    public static string Current { get; } = Read();

    static string Read()
    {
        var info = typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? info : info[..plus];
    }
}
