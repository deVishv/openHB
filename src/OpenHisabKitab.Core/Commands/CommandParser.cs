namespace OpenHisabKitab.Core.Commands;

public sealed record ParsedCommand(string Code, IReadOnlyList<string> Args);

/// <summary>Parses "CODE arg arg" typed at the prompt.</summary>
public static class CommandParser
{
    /// <summary>A code is 4 characters, letters A to Z or digits, starting with a letter. Case is ignored.</summary>
    public static bool TryParse(string? input, out ParsedCommand command)
    {
        command = new ParsedCommand("", []);
        var words = Text.Words(input);
        if (words.Length == 0) return false;
        var code = words[0].ToUpperInvariant();
        if (!IsCode(code)) return false;
        command = new ParsedCommand(code, words[1..]);
        return true;
    }

    static bool IsCode(string s) =>
        s.Length == 4 && char.IsAsciiLetterUpper(s[0]) && s.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c));
}
