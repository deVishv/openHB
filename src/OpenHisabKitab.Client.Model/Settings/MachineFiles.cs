using System.Text.Json;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Settings;

/// <summary>
/// This PC's own files (spec 7, the machine layer). Loading never rewrites a file, so a hand edit with a mistake stays as
/// the user wrote it until they save that layer.
/// </summary>
public sealed class MachineFiles(ClientPaths paths)
{
    static readonly JsonSerializerOptions StateJson = new() { WriteIndented = true };

    public SettingsLoad LoadSettings() =>
        File.Exists(paths.Settings) ? SettingsDocument.Parse(File.ReadAllText(paths.Settings), SettingLayer.Machine) : new SettingsLoad(new Dictionary<string, string>(), []);

    public void SaveSettings(IReadOnlyDictionary<string, string> values) => WriteSettingsText(SettingsDocument.Write(values));

    public KeybindingLoad LoadKeybindings() =>
        File.Exists(paths.Keybindings) ? KeybindingFile.Parse(File.ReadAllText(paths.Keybindings)) : new KeybindingLoad([], []);

    public void SaveKeybindings(IReadOnlyList<KeybindingEntry> entries) => WriteKeybindingsText(KeybindingFile.Write(entries));

    /// <summary>The file as the user wrote it, for the JSON editor; "{}" when there is none.</summary>
    public string ReadSettingsText() => File.Exists(paths.Settings) ? File.ReadAllText(paths.Settings) : "{}";

    public string ReadKeybindingsText() => File.Exists(paths.Keybindings) ? File.ReadAllText(paths.Keybindings) : "[]";

    public void WriteSettingsText(string text) => Write(paths.Settings, text);

    public void WriteKeybindingsText(string text) => Write(paths.Keybindings, text);

    public ClientState LoadState()
    {
        try
        {
            return File.Exists(paths.State) ? JsonSerializer.Deserialize<ClientState>(File.ReadAllText(paths.State)) ?? ClientState.Empty : ClientState.Empty;
        }
        catch (JsonException) { return ClientState.Empty; }
    }

    public void SaveState(ClientState state) => Write(paths.State, JsonSerializer.Serialize(state, StateJson));

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
