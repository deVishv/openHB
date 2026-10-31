using System.Text;
using System.Text.Json;

namespace OpenHisabKitab.Core.Settings;

/// <summary>A problem in a settings or keybindings file, with its 1-based line.</summary>
public sealed record SettingError(int Line, string Message);

/// <summary>A layer's values as raw JSON text per key, and the entries that were skipped.</summary>
public sealed record SettingsLoad(IReadOnlyDictionary<string, string> Values, IReadOnlyList<SettingError> Errors);

/// <summary>One layer as a JSON object, read the way VS Code reads settings: comments and trailing commas are allowed.</summary>
public static class SettingsDocument
{
    public const string NotJson = "This file is not valid JSON.";

    internal static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Each bad entry is skipped and named with its line; the rest apply. Broken JSON gives no values and one error.</summary>
    public static SettingsLoad Parse(string text, SettingLayer layer)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var errors = new List<SettingError>();
        try
        {
            var reader = new Utf8JsonReader(bytes, ReaderOptions);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return Broken(Line(bytes, reader.TokenStartIndex));
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var line = Line(bytes, reader.TokenStartIndex);
                var key = reader.GetString()!;
                reader.Read();
                using var value = JsonDocument.ParseValue(ref reader);
                var json = value.RootElement.GetRawText();
                if (SettingsCatalog.Validate(key, json, layer) is { } problem) errors.Add(new SettingError(line, problem));
                else values[key] = json;
            }
            if (reader.TokenType != JsonTokenType.EndObject || reader.Read()) return Broken(Line(bytes, reader.TokenStartIndex));
        }
        catch (JsonException e) { return Broken((int)(e.LineNumber ?? 0) + 1); }
        return new SettingsLoad(values, errors);
    }

    public static string Write(IReadOnlyDictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var (key, json) in values.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                w.WritePropertyName(key);
                w.WriteRawValue(json);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static SettingsLoad Broken(int line) => new(new Dictionary<string, string>(), [new SettingError(line, NotJson)]);

    /// <summary>The 1-based line of a byte offset.</summary>
    internal static int Line(byte[] bytes, long offset)
    {
        var line = 1;
        for (var i = 0; i < offset && i < bytes.Length; i++) if (bytes[i] == (byte)'\n') line++;
        return line;
    }
}
