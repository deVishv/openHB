namespace OpenHisabKitab.Client.Model.Settings;

public sealed record WindowPlacement(int Left, int Top, int Width, int Height, bool Maximised);

/// <summary>What this PC remembers between runs that no one types: the last host, the last company per host, and the window.</summary>
public sealed record ClientState(string? LastHost, IReadOnlyDictionary<string, int> LastCompanyByHost, WindowPlacement? Window)
{
    public static ClientState Empty { get; } = new(null, new Dictionary<string, int>(), null);
}
