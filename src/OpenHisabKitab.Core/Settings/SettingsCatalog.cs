using System.Text.Json;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Core.Settings;

/// <summary>
/// Every setting the build reads (spec 7, Coverage). A setting is listed only when something reads it; later plans add theirs.
/// The host validates company and user values with this list, so client and host agree.
/// </summary>
public static class SettingsCatalog
{
    const SettingScope MachineOrUser = SettingScope.Machine | SettingScope.User;

    public static IReadOnlyList<SettingDefinition> All { get; } =
    [
        new("connection.host", SettingType.Text, "\"\"", "Host name or IP address.", SettingScope.Machine),
        new("connection.port", SettingType.Integer, "47250", "Host port.", SettingScope.Machine, 1, 65535),
        new("ui.fontFamily", SettingType.Choice, "\"Segoe UI\"", "Font for screen text.", MachineOrUser, Choices: ["Segoe UI", "Tahoma"]),
        new("ui.fontSize", SettingType.Integer, "14", "Text size in pixels.", MachineOrUser, 12, 28),
        new("ui.negativeAmounts", SettingType.Choice, "\"brackets\"", "How negative amounts are shown.", MachineOrUser, Choices: ["brackets", "minus"]),
        new("ui.dateFormat", SettingType.Choice, "\"dd-mm-yyyy\"", "How dates are shown.", MachineOrUser, Choices: ["dd-mm-yyyy", "dd/mm/yyyy", "yyyy-mm-dd"]),
        new("reports.autoReload", SettingType.Boolean, "false", "Reload open reports when data changes.", MachineOrUser),
        new("invoice.roundOff", SettingType.Boolean, "true", "Round invoice totals to the rupee.", SettingScope.Company),
        new("invoice.pricesIncludeTax", SettingType.Boolean, "false", "New invoice lines take prices with tax included.", SettingScope.Company),
        new("outstanding.ageingDays", SettingType.Text, "\"30,60,90\"", "Ageing columns in OUTS, as days overdue.", SettingScope.Company,
            Rule: text => Bills.AgeingBuckets.TryParse(text, out _),
            RuleText: "Setting outstanding.ageingDays must be 1 to 5 rising whole numbers of days, such as 30,60,90."),
    ];

    public static SettingDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);

    /// <summary>Keys containing the text, or descriptions with a word starting with it, ignoring case. Blank text gives all.</summary>
    public static IReadOnlyList<SettingDefinition> Search(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return All;
        return All.Where(d => d.Key.Contains(t, StringComparison.OrdinalIgnoreCase)
            || Text.Words(d.Description).Any(w => w.StartsWith(t, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>Null when the JSON value may be stored for the key in the layer; otherwise the one line reason.</summary>
    public static string? Validate(string key, string json, SettingLayer layer)
    {
        if (Find(key) is not { } d) return $"No setting named {key}.";
        JsonElement value;
        try
        {
            using var doc = JsonDocument.Parse(json);
            value = doc.RootElement.Clone();
        }
        catch (JsonException) { return $"Setting {key} needs a JSON value."; }
        if (!d.Scope.HasFlag(ScopeOf(layer))) return $"Setting {key} cannot be set for {LayerWords(layer)}.";
        return d.Type switch
        {
            SettingType.Boolean when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) => $"Setting {key} must be true or false.",
            SettingType.Integer when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n < d.Min || n > d.Max =>
                $"Setting {key} must be a whole number from {d.Min} to {d.Max}.",
            SettingType.Text when value.ValueKind != JsonValueKind.String => $"Setting {key} must be text.",
            SettingType.Text when d.Rule is { } rule && !rule(value.GetString()!) => d.RuleText,
            SettingType.Choice when value.ValueKind != JsonValueKind.String || !d.Choices!.Contains(value.GetString()!) =>
                $"Setting {key} must be one of: {string.Join(", ", d.Choices!)}.",
            _ => null,
        };
    }

    static SettingScope ScopeOf(SettingLayer layer) => layer switch
    {
        SettingLayer.Machine => SettingScope.Machine,
        SettingLayer.Company => SettingScope.Company,
        SettingLayer.User => SettingScope.User,
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    static string LayerWords(SettingLayer layer) => layer switch
    {
        SettingLayer.Machine => "this PC",
        SettingLayer.Company => "a company",
        _ => "a user",
    };
}
