using System.Globalization;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Settings;

/// <summary>
/// The keybindings in force (spec 6.5): built in, then this PC's keybindings.json, then the user's own, kept on the host as
/// the user setting "keybindings" so they follow the user to any PC.
/// </summary>
public sealed class KeybindingStore(MachineFiles files, HostConnection host)
{
    const string UserKey = "keybindings";

    IReadOnlyList<KeybindingEntry> machine = [];
    IReadOnlyList<KeybindingEntry> user = [];
    string? userText;

    public KeybindingSet Current { get; private set; } = KeybindingSet.Build([], []);

    /// <summary>Problems in this PC's keybindings.json, each as "keybindings.json line {n}: {message}".</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    public event Action? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var load = files.LoadKeybindings();
        machine = load.Entries;
        LoadErrors = [.. load.Errors.Select(e => string.Create(CultureInfo.InvariantCulture, $"keybindings.json line {e.Line}: {e.Message}"))];
        userText = null;
        if (host.State == ConnectionState.Connected)
            try
            {
                var values = await host.CallAsync((h, o) => h.UserSettings.ListAsync(new Empty(), o), ct);
                userText = values.Values.FirstOrDefault(v => v.Key == UserKey)?.Json;
            }
            catch (HostException) { }
        user = userText is null ? [] : KeybindingFile.Parse(userText).Entries;
        Rebuild();
    }

    public string LayerText(SettingLayer layer) => layer switch
    {
        SettingLayer.Machine => files.ReadKeybindingsText(),
        SettingLayer.User => userText ?? "[]",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    /// <summary>Saves a whole layer typed as JSON, as typed. Any error saves nothing.</summary>
    public async Task<IReadOnlyList<SettingError>> SaveLayerTextAsync(SettingLayer layer, string text)
    {
        var load = KeybindingFile.Parse(text);
        if (load.Errors.Count > 0) return load.Errors;
        switch (layer)
        {
            case SettingLayer.Machine:
                files.WriteKeybindingsText(text);
                machine = load.Entries;
                LoadErrors = [];
                break;
            case SettingLayer.User:
                await host.CallAsync((h, o) => h.UserSettings.SetAsync(new SettingValue { Key = UserKey, Json = text }, o));
                userText = text;
                user = load.Entries;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
        Rebuild();
        return [];
    }

    public Task SetEntriesAsync(SettingLayer layer, IReadOnlyList<KeybindingEntry> entries) =>
        SaveLayerTextAsync(layer, KeybindingFile.Write(entries));

    void Rebuild()
    {
        Current = KeybindingSet.Build(machine, user);
        Changed?.Invoke();
    }
}
