namespace OpenHisabKitab.Core.Commands.Keys;

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

/// <summary>One key with its modifiers, written as VS Code writes it: "ctrl+shift+p".</summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    static readonly HashSet<string> Named =
    [
        "enter", "escape", "tab", "space", "backspace", "delete", "insert", "home", "end", "pageup", "pagedown",
        "up", "down", "left", "right",
        .. Enumerable.Range(1, 12).Select(n => "f" + n),
    ];

    /// <summary>Modifiers in any order and any case, then one key: a to z, 0 to 9, f1 to f12, or a named key.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+').Select(p => p.Trim().ToLowerInvariant()).ToArray();
        var modifiers = KeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var m = part switch { "ctrl" => KeyModifiers.Ctrl, "shift" => KeyModifiers.Shift, "alt" => KeyModifiers.Alt, _ => KeyModifiers.None };
            if (m == KeyModifiers.None || modifiers.HasFlag(m)) return false;
            modifiers |= m;
        }
        var key = parts[^1];
        if (!IsKey(key)) return false;
        chord = new KeyChord(modifiers, key);
        return true;
    }

    static bool IsKey(string key) =>
        (key.Length == 1 && (char.IsAsciiLetterLower(key[0]) || char.IsAsciiDigit(key[0]))) || Named.Contains(key);

    public override string ToString()
    {
        var s = "";
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) s += "ctrl+";
        if (Modifiers.HasFlag(KeyModifiers.Shift)) s += "shift+";
        if (Modifiers.HasFlag(KeyModifiers.Alt)) s += "alt+";
        return s + Key;
    }
}
