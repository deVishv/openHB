namespace OpenHisabKitab.Client.Model.Connection;

/// <summary>Where this PC keeps its machine files: settings, keybindings, trusted hosts, and window state.</summary>
public sealed record ClientPaths(string Root)
{
    /// <summary>%LOCALAPPDATA%\OpenHisabKitab, created if missing.</summary>
    public static ClientPaths Default()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenHisabKitab");
        Directory.CreateDirectory(root);
        return new ClientPaths(root);
    }

    public string Settings => Path.Combine(Root, "settings.json");
    public string Keybindings => Path.Combine(Root, "keybindings.json");
    public string KnownHosts => Path.Combine(Root, "known-hosts.json");
    public string State => Path.Combine(Root, "state.json");
}
