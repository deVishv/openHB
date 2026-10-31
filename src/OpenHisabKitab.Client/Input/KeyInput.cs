using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Input;

/// <summary>WPF keys to keybinding chords, and the "when" context of the focused control (spec 6.5).</summary>
public static class KeyInput
{
    /// <summary>
    /// Null for a key no binding can name, such as a bare modifier. WPF reports Alt chords and F10 as Key.System with the
    /// real key in SystemKey, so F10 reaches its binding (run.JRNL) instead of opening the menu; a bare Alt still opens it.
    /// </summary>
    public static KeyChord? From(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var name = Name(key);
        if (name is null) return null;
        var m = KeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) m |= KeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) m |= KeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) m |= KeyModifiers.Alt;
        return new KeyChord(m, name);
    }

    public static IReadOnlySet<string> Context(IInputElement? focused, bool paletteOpen)
    {
        var context = new HashSet<string>();
        if (paletteOpen) context.Add("paletteOpen");
        switch (focused)
        {
            case TextBox { Name: "Prompt" }: context.Add("promptFocus"); break;
            case TextBoxBase or PasswordBox or ComboBox: context.Add("inputFocus"); break;
        }
        if (focused is DependencyObject d && (Ancestor<DataGrid>(d) is not null || Views.EntryGrid.Owner(d) is not null)) context.Add("gridFocus");
        return context;
    }

    static string? Name(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F12 => "f" + (key - Key.F1 + 1),
        Key.Enter => "enter",
        Key.Escape => "escape",
        Key.Tab => "tab",
        Key.Space => "space",
        Key.Back => "backspace",
        Key.Delete => "delete",
        Key.Insert => "insert",
        Key.Home => "home",
        Key.End => "end",
        Key.PageUp => "pageup",
        Key.PageDown => "pagedown",
        Key.Up => "up",
        Key.Down => "down",
        Key.Left => "left",
        Key.Right => "right",
        _ => null,
    };

    static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
    {
        for (var current = d; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
