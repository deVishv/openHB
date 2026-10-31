using System.Globalization;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>What a typed command leads to: a screen, a status line, a refresh of the active tab, or nothing.</summary>
public sealed record RouteResult(IScreen? Screen, string? Status, bool Refresh, string? Command);

/// <summary>
/// Turns prompt text into a screen (spec 6.2). A factory may act and return null, as DATE with a valid date does. Codes in
/// the catalog with no factory yet say so in one line.
/// </summary>
public sealed class FunctionRouter(IReadOnlyDictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> built)
{
    public RouteResult Route(string input, ShellContext ctx)
    {
        var words = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new RouteResult(null, null, false, null);
        if (!CommandParser.TryParse(input, out var cmd) || FunctionCatalog.Find(cmd.Code) is null)
            return new RouteResult(null, Format(Resources.Text.UnknownFunction, words[0].ToUpperInvariant()), false, null);
        if (cmd.Code == "RFSH") return new RouteResult(null, null, true, null);
        if (!built.TryGetValue(cmd.Code, out var factory)) return new RouteResult(null, Format(Resources.Text.NotInThisVersion, cmd.Code), false, null);
        var command = string.Join(' ', [cmd.Code, .. cmd.Args]);
        return new RouteResult(factory(cmd, ctx), null, false, command);
    }

    static string Format(string format, string arg) => string.Format(CultureInfo.InvariantCulture, format, arg);
}
