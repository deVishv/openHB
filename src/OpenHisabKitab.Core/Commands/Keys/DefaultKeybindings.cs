namespace OpenHisabKitab.Core.Commands.Keys;

/// <summary>The built in keybindings: spec 6.5 and the F key row of spec 6.1.</summary>
public static class DefaultKeybindings
{
    static readonly string[] FKeys = ["HELP", "DATE", "FIND", "CMPY", "RFSH", "SALE", "PURC", "RCPT", "PYMT", "JRNL", "PRNT", "SETG"];

    public static IReadOnlyList<KeybindingEntry> All { get; } =
    [
        Bind("ctrl+l", "prompt.focus"),
        Bind("enter", "prompt.run", "promptFocus"),
        Bind("shift+enter", "prompt.runNewTab", "promptFocus"),
        Bind("escape", "screen.cancel", "!paletteOpen"),
        Bind("ctrl+t", "tab.new"),
        Bind("ctrl+w", "tab.close"),
        Bind("ctrl+tab", "tab.next"),
        Bind("ctrl+shift+tab", "tab.previous"),
        .. Enumerable.Range(1, 9).Select(n => Bind($"ctrl+{n}", $"tab.goto.{n}")),
        Bind("alt+left", "nav.back"),
        Bind("alt+right", "nav.forward"),
        Bind("ctrl+s", "screen.save"),
        Bind("ctrl+shift+p", "palette.open"),
        Bind("ctrl+f", "screen.find"),
        Bind("ctrl+n", "record.new"),
        Bind("ctrl+delete", "record.delete", "!gridFocus && !inputFocus"),
        Bind("ctrl+insert", "line.add", "gridFocus"),
        Bind("ctrl+delete", "line.remove", "gridFocus"),
        Bind("ctrl+b", "voucher.bills"),
        Bind("ctrl+shift+delete", "voucher.cancelVoucher"),
        .. FKeys.Select((code, i) => Bind($"f{i + 1}", "run." + code)),
    ];

    static KeybindingEntry Bind(string key, string command, string? when = null)
    {
        if (!KeyChord.TryParse(key, out var chord)) throw new InvalidOperationException($"Bad default key {key}.");
        if (!WhenClause.TryParse(when, out var clause, out var error)) throw new InvalidOperationException(error);
        return new KeybindingEntry(chord, command, clause, Remove: false);
    }
}
