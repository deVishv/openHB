using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>One F key button: "F6" and the code it runs, or no code when the key runs no function.</summary>
public sealed record FKeyLabel(string Key, string? Code);

/// <summary>The twelve F key buttons (spec 6.1), labelled from the keybindings in force so a rebound key relabels.</summary>
public static class FKeyRow
{
    static readonly IReadOnlySet<string> NoContext = new HashSet<string>();

    public static IReadOnlyList<FKeyLabel> From(KeybindingSet keys) =>
    [
        .. Enumerable.Range(1, 12).Select(n =>
        {
            var command = keys.Resolve(new KeyChord(KeyModifiers.None, $"f{n}"), NoContext);
            return new FKeyLabel($"F{n}", command is not null && command.StartsWith("run.", StringComparison.Ordinal) ? command[4..] : null);
        }),
    ];
}
