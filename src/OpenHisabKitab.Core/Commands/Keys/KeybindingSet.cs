using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Commands.Keys;

public sealed record Keybinding(KeyChord Key, string Command, WhenClause When, SettingLayer From);

/// <summary>
/// The keybindings in force (spec 6.5), layered as VS Code layers them: defaults, then machine, then user. Within a layer a
/// later entry beats an earlier one, and a "-command" entry removes the earlier bindings of that command on that key.
/// </summary>
public sealed class KeybindingSet
{
    KeybindingSet(IReadOnlyList<Keybinding> all) => All = all;

    /// <summary>Highest precedence first.</summary>
    public IReadOnlyList<Keybinding> All { get; }

    public static KeybindingSet Build(IReadOnlyList<KeybindingEntry> machine, IReadOnlyList<KeybindingEntry> user)
    {
        var active = new List<Keybinding>();
        foreach (var (layer, entries) in new[] { (SettingLayer.Default, DefaultKeybindings.All), (SettingLayer.Machine, machine), (SettingLayer.User, user) })
            foreach (var e in entries)
            {
                if (e.Remove)
                    active.RemoveAll(b => b.Command == e.Command && b.Key == e.Key
                        && (e.When == WhenClause.Always || b.When.ToString() == e.When.ToString()));
                else active.Add(new Keybinding(e.Key, e.Command, e.When, layer));
            }
        active.Reverse();
        return new KeybindingSet(active);
    }

    public string? Resolve(KeyChord key, IReadOnlySet<string> context) =>
        All.FirstOrDefault(b => b.Key == key && b.When.Matches(context))?.Command;

    /// <summary>The key shown beside a command in menus and the F key row: its first unconditional binding no other command has taken.</summary>
    public KeyChord? KeyFor(string command)
    {
        var taken = new HashSet<KeyChord>();
        foreach (var b in All.Where(b => b.When == WhenClause.Always))
        {
            if (b.Command == command && !taken.Contains(b.Key)) return b.Key;
            taken.Add(b.Key);
        }
        return null;
    }
}
