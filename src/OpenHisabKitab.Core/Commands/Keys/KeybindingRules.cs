namespace OpenHisabKitab.Core.Commands.Keys;

/// <summary>
/// Keys a binding may not take. Typing keys must reach fields and the prompt, and Alt with a letter must reach the menus,
/// so the menu always opens KEYS to undo a bad binding (spec 6.5).
/// </summary>
public static class KeybindingRules
{
    static readonly HashSet<string> Typing =
        ["space", "tab", "backspace", "delete", "home", "end", "left", "right", "up", "down", "pageup", "pagedown"];

    /// <summary>Null when the key can be bound; otherwise the one line reason.</summary>
    public static string? Problem(KeyChord key)
    {
        var plain = key.Modifiers is KeyModifiers.None or KeyModifiers.Shift;
        var printable = key.Key.Length == 1;
        if (plain && (printable || Typing.Contains(key.Key))) return $"Cannot bind {key}: it is needed for typing.";
        if (key.Modifiers == KeyModifiers.Alt && printable && char.IsAsciiLetter(key.Key[0])) return $"Cannot bind {key}: Alt with a letter opens the menus.";
        return null;
    }
}
