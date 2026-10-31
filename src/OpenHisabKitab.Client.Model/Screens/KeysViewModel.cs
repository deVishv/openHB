using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Commands.Keys;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>One keybinding as KEYS lists it.</summary>
public sealed record KeyRow(KeyChord Chord, string Key, string Command, string CommandName, WhenClause WhenClause, string When, SettingLayer From)
{
    public string FromText => SettingsViewModel.LayerName(From);
}

/// <summary>
/// KEYS: every keybinding, searchable, changed by pressing the new key or edited as JSON per layer (spec 6.5). A change
/// is written VS Code style: the old key unbound, the new key bound.
/// </summary>
public sealed partial class KeysViewModel : ScreenViewModel, IKeyRecorder
{
    IReadOnlyList<KeyRow> all = [];

    KeysViewModel(ShellContext ctx) : base(ctx)
    {
        TargetLayer = ctx.Host.Session is null ? SettingLayer.Machine : SettingLayer.User;
        JsonLayer = TargetLayer;
        ctx.Keys.Changed += Rebuild;
    }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new KeysViewModel(ctx) { Filter = string.Join(' ', cmd.Args) };

    public override string Code => "KEYS";

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<KeyRow> Rows { get; private set; } = [];

    [ObservableProperty]
    public partial KeyRow? Selected { get; set; }

    [ObservableProperty]
    public partial bool Recording { get; private set; }

    /// <summary>Machine or User: where a change is saved.</summary>
    [ObservableProperty]
    public partial SettingLayer TargetLayer { get; set; }

    public IReadOnlyList<SettingLayer> Layers => Ctx.Host.Session is null ? [SettingLayer.Machine] : [SettingLayer.Machine, SettingLayer.User];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial bool JsonMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial SettingLayer JsonLayer { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedEdits))]
    public partial string JsonText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> JsonErrors { get; private set; } = [];

    public override bool HasUnsavedEdits => JsonMode && JsonText != Ctx.Keys.LayerText(JsonLayer);

    public override async Task LoadAsync(CancellationToken ct)
    {
        await RunAsync(() => Ctx.Keys.LoadAsync(ct));
        JsonText = Ctx.Keys.LayerText(JsonLayer);
        Rebuild();
    }

    /// <summary>Enter on a row: the next key pressed becomes that command's key.</summary>
    public void StartRecording()
    {
        if (Selected is null) return;
        Recording = true;
        Message = Text.Recording;
    }

    public async Task RecordKeyAsync(KeyChord key)
    {
        if (!Recording) return;
        Recording = false;
        Message = null;
        if (key == new KeyChord(KeyModifiers.None, "escape") || Selected is not { } row) return;
        List<KeybindingEntry> entries =
        [
            .. LayerEntries(TargetLayer),
            new(row.Chord, row.Command, row.WhenClause, Remove: true),
            new(key, row.Command, row.WhenClause, Remove: false),
        ];
        var layer = TargetLayer;
        await RunAsync(() => Ctx.Keys.SetEntriesAsync(layer, entries));
    }

    /// <summary>Back to the key of the layers below: this layer's entries for the command are removed.</summary>
    public Task ResetAsync(KeyRow row)
    {
        var layer = TargetLayer;
        var entries = LayerEntries(layer).Where(e => e.Command != row.Command).ToList();
        return RunAsync(() => Ctx.Keys.SetEntriesAsync(layer, entries));
    }

    public override async Task SaveAsync()
    {
        if (!JsonMode) return;
        IReadOnlyList<SettingError> errors = [];
        if (!await RunAsync(async () => errors = await Ctx.Keys.SaveLayerTextAsync(JsonLayer, JsonText))) return;
        JsonErrors = [.. errors.Select(e => string.Format(CultureInfo.InvariantCulture, Text.JsonLine, e.Line, e.Message))];
        if (errors.Count == 0)
        {
            Message = Text.Saved;
            JsonText = Ctx.Keys.LayerText(JsonLayer);   // the layer as stored
        }
    }

    public event Action? FocusFilterRequested;

    public override void Find() => FocusFilterRequested?.Invoke();

    public override bool Cancel()
    {
        if (Recording)
        {
            Recording = false;
            Message = null;
            return true;
        }
        if (Filter.Length == 0) return false;
        Filter = "";
        return true;
    }

    partial void OnFilterChanged(string value) => Apply();

    partial void OnJsonLayerChanged(SettingLayer value)
    {
        JsonText = Ctx.Keys.LayerText(value);
        JsonErrors = [];
    }

    IReadOnlyList<KeybindingEntry> LayerEntries(SettingLayer layer) => KeybindingFile.Parse(Ctx.Keys.LayerText(layer)).Entries;

    void Rebuild()
    {
        var selected = Selected?.Command;
        all = [.. Ctx.Keys.Current.All.Select(b => new KeyRow(b.Key, KeyText.Display(b.Key), b.Command, HelpViewModel.CommandName(b.Command), b.When, b.When.ToString(), b.From))];
        Apply();
        Selected = Rows.FirstOrDefault(r => r.Command == selected);
    }

    void Apply()
    {
        var f = Filter.Trim();
        Rows = f.Length == 0 ? all
            : [.. all.Where(r => r.Key.Contains(f, StringComparison.OrdinalIgnoreCase) || r.CommandName.Contains(f, StringComparison.OrdinalIgnoreCase)
                || r.Command.Contains(f, StringComparison.OrdinalIgnoreCase))];
    }
}
