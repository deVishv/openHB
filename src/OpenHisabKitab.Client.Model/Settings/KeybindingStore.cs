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
    bool userRead;
    IReadOnlyList<SettingError> machineErrors = [];

    /// <summary>The problems in this PC's keybindings.json, by line, for the JSON editor.</summary>
    public IReadOnlyList<SettingError> MachineErrors => machineErrors;

    public KeybindingSet Current { get; private set; } = KeybindingSet.Build([], []);

    /// <summary>Problems in this PC's keybindings.json, each as "keybindings.json line {n}: {message}".</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    public event Action? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var load = files.LoadKeybindings();
        machine = load.Entries;
        machineErrors = load.Errors;
        LoadErrors = [.. load.Errors.Select(e => string.Format(CultureInfo.InvariantCulture, Resources.Text.FileLine, "keybindings.json", e.Line, e.Message))];
        if (host.Session is null) (userText, userRead) = (null, false);
        else if (host.State == ConnectionState.Connected)
            try
            {
                var values = await host.CallAsync((h, o) => h.UserSettings.ListAsync(new Empty(), o), ct);
                (userText, userRead) = (values.Values.FirstOrDefault(v => v.Key == UserKey)?.Json, true);
            }
            catch (HostException) { }   // keeps what was last read
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
                machineErrors = [];
                break;
            case SettingLayer.User:
                // An unread layer is never written: the save would replace the user's keybindings on the host.
                if (!userRead) throw new HostException(HostErrorKind.Unreachable, Resources.Text.LayerNotRead, new Grpc.Core.Metadata());
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
