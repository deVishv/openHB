using System.Text;
using System.Text.Json;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Commands.Keys;

/// <summary>One keybindings.json entry. <see cref="Remove"/> is a "-command" entry, which unbinds rather than binds.</summary>
public sealed record KeybindingEntry(KeyChord Key, string Command, WhenClause When, bool Remove);

public sealed record KeybindingLoad(IReadOnlyList<KeybindingEntry> Entries, IReadOnlyList<SettingError> Errors);

/// <summary>keybindings.json: a JSON array of { "key", "command", "when"? }, read with comments and trailing commas allowed.</summary>
public static class KeybindingFile
{
    const string NeedsKeyAndCommand = "Each entry needs \"key\" and \"command\".";

    /// <summary>Each bad entry is skipped and named with its line; the rest apply. Broken JSON gives no entries and one error.</summary>
    public static KeybindingLoad Parse(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var entries = new List<KeybindingEntry>();
        var errors = new List<SettingError>();
        try
        {
            var reader = new Utf8JsonReader(bytes, SettingsDocument.ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) return Broken(SettingsDocument.Line(bytes, reader.TokenStartIndex));
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var line = SettingsDocument.Line(bytes, reader.TokenStartIndex);
                using var element = JsonDocument.ParseValue(ref reader);
                if (Entry(element.RootElement, out var entry, out var problem)) entries.Add(entry);
                else errors.Add(new SettingError(line, problem!));
            }
            if (reader.TokenType != JsonTokenType.EndArray || reader.Read()) return Broken(SettingsDocument.Line(bytes, reader.TokenStartIndex));
        }
        catch (JsonException e) { return Broken((int)(e.LineNumber ?? 0) + 1); }
        return new KeybindingLoad(entries, errors);
    }

    public static string Write(IReadOnlyList<KeybindingEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartArray();
            foreach (var e in entries)
            {
                w.WriteStartObject();
                w.WriteString("key", e.Key.ToString());
                w.WriteString("command", (e.Remove ? "-" : "") + e.Command);
                if (e.When != WhenClause.Always) w.WriteString("when", e.When.ToString());
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static bool Entry(JsonElement e, out KeybindingEntry entry, out string? problem)
    {
        entry = null!;
        problem = NeedsKeyAndCommand;
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("key", out var k) || k.ValueKind != JsonValueKind.String
            || !e.TryGetProperty("command", out var c) || c.ValueKind != JsonValueKind.String) return false;
        var command = c.GetString()!;
        var remove = command.StartsWith('-');
        if (remove) command = command[1..];
        if (!KeyCommands.IsKnown(command)) { problem = $"Unknown command: {command}."; return false; }
        if (!KeyChord.TryParse(k.GetString(), out var key)) { problem = $"Cannot read key: {k.GetString()}."; return false; }
        if (!remove && KeybindingRules.Problem(key) is { } refused) { problem = refused; return false; }
        var whenText = e.TryGetProperty("when", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
        if (!WhenClause.TryParse(whenText, out var when, out problem)) return false;
        entry = new KeybindingEntry(key, command, when, remove);
        return true;
    }

    static KeybindingLoad Broken(int line) => new([], [new SettingError(line, SettingsDocument.NotJson)]);
}
