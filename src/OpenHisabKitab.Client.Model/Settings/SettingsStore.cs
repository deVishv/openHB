using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Settings;

/// <summary>
/// The settings in force on this client (spec 7): built in defaults, this PC's file, the open company, and the user's own,
/// each overriding the one before. A layer the client cannot read, such as the user layer while logged out, is empty.
/// </summary>
public sealed class SettingsStore(MachineFiles files, HostConnection host)
{
    Dictionary<string, string> machine = [];
    IReadOnlyDictionary<string, string> company = new Dictionary<string, string>();
    IReadOnlyDictionary<string, string> user = new Dictionary<string, string>();

    public EffectiveSettings Current { get; private set; } = new(new Dictionary<SettingLayer, IReadOnlyDictionary<string, string>>());

    /// <summary>Problems in this PC's settings.json, each as "settings.json line {n}: {message}".</summary>
    public IReadOnlyList<string> LoadErrors { get; private set; } = [];

    public event Action? Changed;

    public async Task LoadAsync(int? company, CancellationToken ct = default)
    {
        var load = files.LoadSettings();
        machine = new Dictionary<string, string>(load.Values, StringComparer.Ordinal);
        LoadErrors = [.. load.Errors.Select(e => string.Create(CultureInfo.InvariantCulture, $"settings.json line {e.Line}: {e.Message}"))];
        user = await ReadAsync(async () => (await host.CallAsync((h, o) => h.UserSettings.ListAsync(new Empty(), o), ct)).Values);
        this.company = company is { } id
            ? await ReadAsync(async () => (await host.CallAsync((h, o) => h.Companies.ListSettingsAsync(new CompanyRef { Company = id }, o), ct)).Values)
            : new Dictionary<string, string>();
        Rebuild();
    }

    public async Task SetAsync(string key, string json, SettingLayer layer, int? company)
    {
        if (SettingsCatalog.Validate(key, json, layer) is { } problem) throw new ArgumentException(problem, nameof(json));
        switch (layer)
        {
            case SettingLayer.Machine:
                machine[key] = json;
                EditMachineFile(o => o[key] = JsonNode.Parse(json));
                break;
            case SettingLayer.User:
                await host.CallAsync((h, o) => h.UserSettings.SetAsync(new SettingValue { Key = key, Json = json }, o));
                user = new Dictionary<string, string>(user) { [key] = json };
                break;
            case SettingLayer.Company:
                await host.CallAsync((h, o) => h.Companies.SetSettingAsync(new SettingRequest { Company = CompanyId(company), Key = key, Json = json }, o));
                this.company = new Dictionary<string, string>(this.company) { [key] = json };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
        Rebuild();
    }

    public async Task ResetAsync(string key, SettingLayer layer, int? company)
    {
        switch (layer)
        {
            case SettingLayer.Machine:
                if (machine.Remove(key)) EditMachineFile(o => o.Remove(key));
                break;
            case SettingLayer.User:
                await host.CallAsync((h, o) => h.UserSettings.ResetAsync(new SettingKey { Key = key }, o));
                user = Without(user, key);
                break;
            case SettingLayer.Company:
                await host.CallAsync((h, o) => h.Companies.ResetSettingAsync(new CompanySettingKey { Company = CompanyId(company), Key = key }, o));
                this.company = Without(this.company, key);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(layer));
        }
        Rebuild();
    }

    /// <summary>The layer as JSON for the editor: this PC's file as the user wrote it, or the stored values.</summary>
    public string LayerText(SettingLayer layer) => layer switch
    {
        SettingLayer.Machine => files.ReadSettingsText(),
        SettingLayer.User => SettingsDocument.Write(user),
        SettingLayer.Company => SettingsDocument.Write(company),
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    /// <summary>Saves a whole layer typed as JSON. Any error saves nothing.</summary>
    public async Task<IReadOnlyList<SettingError>> SaveLayerTextAsync(SettingLayer layer, string text, int? company)
    {
        var load = SettingsDocument.Parse(text, layer);
        if (load.Errors.Count > 0) return load.Errors;
        if (layer == SettingLayer.Machine)
        {
            files.WriteSettingsText(text);
            machine = new Dictionary<string, string>(load.Values, StringComparer.Ordinal);
            LoadErrors = [];
            Rebuild();
            return [];
        }
        var stored = layer == SettingLayer.User ? user : this.company;
        foreach (var (key, json) in load.Values.Where(p => stored.GetValueOrDefault(p.Key) != p.Value))
            await SetAsync(key, json, layer, company);
        foreach (var key in stored.Keys.Where(k => !load.Values.ContainsKey(k)).ToList())
            await ResetAsync(key, layer, company);
        return [];
    }

    /// <summary>
    /// Changes one key in this PC's file and keeps every other entry, including ones with errors, so the user still sees
    /// and fixes them. A file that is not valid JSON is left alone; the value applies until the client closes.
    /// </summary>
    void EditMachineFile(Action<JsonObject> change)
    {
        JsonObject? file;
        try
        {
            file = JsonNode.Parse(files.ReadSettingsText(), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            }) as JsonObject;
        }
        catch (JsonException) { file = null; }
        if (file is null) return;
        change(file);
        files.WriteSettingsText(file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    void Rebuild()
    {
        Current = new EffectiveSettings(new Dictionary<SettingLayer, IReadOnlyDictionary<string, string>>
        {
            [SettingLayer.Machine] = machine,
            [SettingLayer.Company] = company,
            [SettingLayer.User] = user,
        });
        Changed?.Invoke();
    }

    async Task<IReadOnlyDictionary<string, string>> ReadAsync(Func<Task<IEnumerable<SettingValue>>> read)
    {
        if (host.State != ConnectionState.Connected) return new Dictionary<string, string>();
        try
        {
            // Only settings: the user layer also stores keybindings, which KeybindingStore reads.
            return (await read()).Where(v => SettingsCatalog.Find(v.Key) is not null).ToDictionary(v => v.Key, v => v.Json, StringComparer.Ordinal);
        }
        catch (HostException) { return new Dictionary<string, string>(); }
    }

    static int CompanyId(int? company) => company ?? throw new InvalidOperationException("Open a company first.");

    static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> values, string key)
    {
        var d = new Dictionary<string, string>(values, StringComparer.Ordinal);
        d.Remove(key);
        return d;
    }
}
