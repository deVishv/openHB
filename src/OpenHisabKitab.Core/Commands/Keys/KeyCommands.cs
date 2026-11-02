namespace OpenHisabKitab.Core.Commands.Keys;

/// <summary>Every command a key can be bound to: the shell's own, then "run.CODE" for each function.</summary>
public static class KeyCommands
{
    public static IReadOnlyList<string> All { get; } =
    [
        "prompt.focus", "prompt.run", "prompt.runNewTab",
        "screen.cancel", "screen.save", "screen.find",
        "tab.new", "tab.close", "tab.next", "tab.previous",
        .. Enumerable.Range(1, 9).Select(n => $"tab.goto.{n}"),
        "nav.back", "nav.forward", "palette.open",
        "record.new", "record.delete", "line.add", "line.remove", "voucher.bills", "voucher.cancelVoucher",
        "report.export", "report.style",
        .. FunctionCatalog.All.Select(f => "run." + f.Code),
    ];

    static readonly HashSet<string> Known = [.. All];

    public static bool IsKnown(string id) => Known.Contains(id);
}
