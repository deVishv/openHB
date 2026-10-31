using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>A key as menus and HELP show it: "Ctrl+Shift+P", "F5", "Alt+Left".</summary>
public static class KeyText
{
    public static string Display(KeyChord chord)
    {
        var parts = new List<string>();
        if (chord.Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (chord.Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (chord.Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(chord.Key switch
        {
            "pageup" => "PageUp",
            "pagedown" => "PageDown",
            { Length: 1 } k => k.ToUpperInvariant(),
            var k => char.ToUpperInvariant(k[0]) + k[1..],
        });
        return string.Join('+', parts);
    }
}
