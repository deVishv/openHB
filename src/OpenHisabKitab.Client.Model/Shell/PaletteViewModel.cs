using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>One palette line and the prompt command it runs.</summary>
public sealed record PaletteItem(string Label, string Command);

/// <summary>Ctrl+Shift+P: search all functions and settings by name (spec 6.5). A setting opens SETG on it.</summary>
public sealed partial class PaletteViewModel(Func<string, Task> run) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<PaletteItem> Items { get; private set; } = [];

    [ObservableProperty]
    public partial int Selected { get; set; }

    public void Open()
    {
        Text = "";
        Refresh();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public async Task AcceptAsync()
    {
        if (Selected < 0 || Selected >= Items.Count) return;
        var command = Items[Selected].Command;
        Close();
        await run(command);
    }

    partial void OnTextChanged(string value) => Refresh();

    void Refresh()
    {
        Items =
        [
            .. FunctionCatalog.Search(Text).Select(f => new PaletteItem($"{f.Code}  {f.Name}", f.Code)),
            .. SettingsCatalog.Search(Text).Select(d => new PaletteItem($"{d.Key}  {d.Description}", "SETG " + d.Key)),
        ];
        Selected = 0;
    }
}
