using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>One setting as SETG lists it: its value in force, where that comes from, and the layers this user may write.</summary>
public sealed partial class SettingRow(SettingDefinition definition, string json, SettingLayer from, bool modified, IReadOnlyList<SettingLayer> layers)
    : ObservableObject
{
    public SettingDefinition Definition { get; } = definition;
    public string Key => Definition.Key;
    public string Category => Definition.Category;
    public string Description => Definition.Description;
    public string Json { get; } = json;
    public SettingLayer From { get; } = from;
    public bool Modified { get; } = modified;
    public IReadOnlyList<SettingLayer> Layers { get; } = layers;
    public bool CanSet => Layers.Count > 0;

    /// <summary>"*ui.fontSize" when the value differs from the default (spec 7).</summary>
    public string Display => (Modified ? "*" : "") + Key;

    /// <summary>The value as the user reads and types it: text without quotes.</summary>
    public string ValueText => JsonDocument.Parse(Json).RootElement is { ValueKind: JsonValueKind.String } s ? s.GetString()! : Json;

    public string FromText => SettingsViewModel.LayerName(From);

    /// <summary>The layer a change is saved to; the highest this user may write by default.</summary>
    [ObservableProperty]
    public partial SettingLayer TargetLayer { get; set; } = layers.Count > 0 ? layers[^1] : SettingLayer.Default;
}

/// <summary>One host setting (spec 7, Host settings), as the host defines it.</summary>
public sealed record HostSettingRow(string Key, string Description, string AllowedValues, string Value);

/// <summary>
/// SETG: every setting, searchable, as a form or as JSON per layer (spec 7). The Owner also sees the host settings, and
/// is told when a change waits for a host restart.
/// </summary>
public sealed partial class SettingsViewModel : ScreenViewModel, IDisposable
{
    IReadOnlyList<SettingRow> all = [];

    SettingsViewModel(ShellContext ctx, string filter) : base(ctx)
    {
        Filter = filter;
        ctx.Settings.Changed += Rebuild;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new SettingsViewModel(ctx, string.Join(' ', cmd.Args));

    public void Dispose() => Ctx.Settings.Changed -= Rebuild;

    public override string Code => "SETG";

    public override string Subject => Filter;

    [ObservableProperty]
    public partial string Filter { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<SettingRow> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<HostSettingRow> HostRows { get; private set; } = [];

    /// <summary>The row being edited. A reload keeps it, and keeps each row's chosen layer.</summary>
    [ObservableProperty]
    public partial SettingRow? Selected { get; set; }

    public bool CanSeeHost => Ctx.Has("ManageHostSettings");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial bool JsonMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial SettingLayer JsonLayer { get; set; } = SettingLayer.Machine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial string JsonText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> JsonErrors { get; private set; } = [];

    /// <summary>The layers this user may edit as JSON.</summary>
    public IReadOnlyList<SettingLayer> JsonLayers =>
        [SettingLayer.Machine, .. CompanyWritable ? [SettingLayer.Company] : Array.Empty<SettingLayer>(), .. UserWritable ? [SettingLayer.User] : Array.Empty<SettingLayer>()];

    public override bool HasUnsavedEdits => JsonMode && JsonText != Ctx.Settings.LayerText(JsonLayer);

    bool UserWritable => Ctx.Host.Session is not null;

    bool CompanyWritable => Ctx.Has("ManageCompanies") && Ctx.Company is not null;

    public override async Task LoadAsync(CancellationToken ct)
    {
        await RunAsync(async () =>
        {
            await Ctx.Settings.LoadAsync(Ctx.Company?.Id, ct);
            if (CanSeeHost) await LoadHostAsync(ct);
        });
        JsonLayer = JsonLayers[^1];
        JsonText = Ctx.Settings.LayerText(JsonLayer);
        JsonErrors = JsonLayer == SettingLayer.Machine ? Lines(Ctx.Settings.MachineErrors) : [];
        Rebuild();
    }

    /// <summary>Sets a value typed as the user reads it: "Tahoma", "18", "true".</summary>
    public Task SetValueAsync(SettingRow row, string text) => SetAsync(row, ToJson(row.Definition, text.Trim()));

    public async Task SetAsync(SettingRow row, string json)
    {
        if (!row.CanSet) return;
        if (SettingsCatalog.Validate(row.Key, json, row.TargetLayer) is { } problem)
        {
            Message = problem;
            return;
        }
        var layer = row.TargetLayer;
        if (await RunAsync(() => Ctx.Settings.SetAsync(row.Key, json, layer, Ctx.Company?.Id)))
            Message = row.Category == "connection" ? Text.SavedNextConnect : Text.Saved;
    }

    public async Task ResetAsync(SettingRow row)
    {
        if (!row.CanSet) return;
        var layer = row.TargetLayer;
        if (await RunAsync(() => Ctx.Settings.ResetAsync(row.Key, layer, Ctx.Company?.Id))) Message = Text.Saved;
    }

    public async Task SetHostAsync(HostSettingRow row, string json)
    {
        SetHostSettingReply? reply = null;
        if (!await RunAsync(async () =>
            {
                reply = await Ctx.Host.CallAsync((h, o) => h.Admin.SetHostSettingAsync(new HostSettingRequest { Key = row.Key, Json = json }, o));
                await LoadHostAsync(CancellationToken.None);
            })) return;
        Message = reply!.RestartNeeded ? Text.SavedRestartHost : Text.Saved;
    }

    /// <summary>Ctrl+S in JSON mode saves the whole layer; any error saves nothing and lists every line.</summary>
    public override async Task SaveAsync()
    {
        if (!JsonMode) return;
        IReadOnlyList<SettingError> errors = [];
        if (!await RunAsync(async () => errors = await Ctx.Settings.SaveLayerTextAsync(JsonLayer, JsonText, Ctx.Company?.Id))) return;
        JsonErrors = [.. errors.Select(e => string.Format(CultureInfo.InvariantCulture, Text.JsonLine, e.Line, e.Message))];
        if (errors.Count == 0)
        {
            Message = Text.Saved;
            JsonText = Ctx.Settings.LayerText(JsonLayer);   // the layer as stored
        }
    }

    public override void Find() => FocusFilterRequested?.Invoke();

    public event Action? FocusFilterRequested;

    public override bool Cancel()
    {
        if (Filter.Length == 0) return false;
        Filter = "";
        return true;
    }

    partial void OnFilterChanged(string value) => Apply();

    partial void OnJsonLayerChanged(SettingLayer value)
    {
        JsonText = Ctx.Settings.LayerText(value);
        JsonErrors = value == SettingLayer.Machine ? Lines(Ctx.Settings.MachineErrors) : [];
    }

    static IReadOnlyList<string> Lines(IReadOnlyList<SettingError> errors) =>
        [.. errors.Select(e => string.Format(CultureInfo.InvariantCulture, Text.JsonLine, e.Line, e.Message))];

    public static string LayerName(SettingLayer layer) => Text.ResourceManager.GetString("Layer_" + layer, Text.Culture)!;

    async Task LoadHostAsync(CancellationToken ct)
    {
        var list = await Ctx.Host.CallAsync((h, o) => h.Admin.ListHostSettingsAsync(new Empty(), o), ct);
        HostRows = [.. list.Settings.Select(s => new HostSettingRow(s.Key, s.Description, s.AllowedValues, s.Json))];
    }

    void Rebuild()
    {
        var targets = all.ToDictionary(r => r.Key, r => r.TargetLayer);
        var selected = Selected?.Key;
        var current = Ctx.Settings.Current;
        all =
        [
            .. SettingsCatalog.All.OrderBy(d => d.Category, StringComparer.Ordinal).Select(d =>
            {
                var (json, from) = current.Value(d.Key);
                var row = new SettingRow(d, json, from, current.IsModified(d.Key), Writable(d.Scope));
                if (targets.TryGetValue(d.Key, out var target) && row.Layers.Contains(target)) row.TargetLayer = target;
                return row;
            }),
        ];
        Apply();
        Selected = Rows.FirstOrDefault(r => r.Key == selected);
    }

    void Apply()
    {
        var f = Filter.Trim();
        Rows = f.Length == 0 ? all
            : [.. all.Where(r => r.Key.Contains(f, StringComparison.OrdinalIgnoreCase) || r.Description.Contains(f, StringComparison.OrdinalIgnoreCase))];
    }

    IReadOnlyList<SettingLayer> Writable(SettingScope scope)
    {
        var layers = new List<SettingLayer>();
        if (scope.HasFlag(SettingScope.Machine)) layers.Add(SettingLayer.Machine);
        if (scope.HasFlag(SettingScope.Company) && CompanyWritable) layers.Add(SettingLayer.Company);
        if (scope.HasFlag(SettingScope.User) && UserWritable) layers.Add(SettingLayer.User);
        return layers;
    }

    static string ToJson(SettingDefinition d, string text) => d.Type switch
    {
        SettingType.Text or SettingType.Choice => JsonSerializer.Serialize(text),
        SettingType.Integer => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n.ToString(CultureInfo.InvariantCulture) : JsonSerializer.Serialize(text),
        _ => text.ToLowerInvariant() is "true" or "false" ? text.ToLowerInvariant() : JsonSerializer.Serialize(text),
    };
}
