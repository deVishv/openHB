namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>Breaks text into lines that fit a width: at spaces, inside a word only when the word alone is too wide.</summary>
public static class TextWrap
{
    public static IReadOnlyList<string> Lines(string text, double width, PrintFont font, ITextMeasure m)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var joined = line.Length == 0 ? word : line + " " + word;
                if (m.Width(joined, font) <= width)
                {
                    line = joined;
                    continue;
                }
                if (line.Length > 0) lines.Add(line);
                line = word;
                // A word wider than the box is cut at the last character that fits; at least one per line.
                while (m.Width(line, font) > width)
                {
                    var fit = 1;
                    while (fit < line.Length && m.Width(line[..(fit + 1)], font) <= width) fit++;
                    lines.Add(line[..fit]);
                    line = line[fit..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }
}
