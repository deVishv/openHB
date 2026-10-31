using System.Text.Json;

namespace OpenHisabKitab.Core.Settings;

/// <summary>The value in force for each setting: the highest layer holding a valid value, else the default (spec 7).</summary>
public sealed class EffectiveSettings(IReadOnlyDictionary<SettingLayer, IReadOnlyDictionary<string, string>> layers)
{
    static readonly SettingLayer[] Highest = [SettingLayer.User, SettingLayer.Company, SettingLayer.Machine];

    public (string Json, SettingLayer From) Value(string key)
    {
        var d = SettingsCatalog.Find(key) ?? throw new ArgumentException($"No setting named {key}.", nameof(key));
        foreach (var layer in Highest)
            if (layers.TryGetValue(layer, out var values) && values.TryGetValue(key, out var json) && SettingsCatalog.Validate(key, json, layer) is null)
                return (json, layer);
        return (d.DefaultJson, SettingLayer.Default);
    }

    public bool Bool(string key) => Parse(key).GetBoolean();

    public int Int(string key) => Parse(key).GetInt32();

    public string Text(string key) => Parse(key).GetString()!;

    public bool IsModified(string key)
    {
        using var current = JsonDocument.Parse(Value(key).Json);
        using var original = JsonDocument.Parse(SettingsCatalog.Find(key)!.DefaultJson);
        return !JsonElement.DeepEquals(current.RootElement, original.RootElement);
    }

    JsonElement Parse(string key)
    {
        using var doc = JsonDocument.Parse(Value(key).Json);
        return doc.RootElement.Clone();
    }
}
