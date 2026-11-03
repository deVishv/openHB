using OpenHisabKitab.Client.Model.Screens.Reports;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>A printed column: its header, width in points, alignment, and whether it holds text or numbers.</summary>
public sealed record TableColumn(string Header, double Width, PrintAlign Align, PrintFace Face);

/// <summary>A printed row: its cells, whether it is a total, and its indent level in the first column.</summary>
public sealed record TableRow(IReadOnlyList<string> Cells, bool Bold = false, int Depth = 0);

/// <summary>
/// Lays a table out across pages: the header row, then the rows, wrapping text cells and never number cells, repeating
/// the header after each page break, and never cutting a row across pages.
/// </summary>
public static class PrintTable
{
    /// <summary>Space inside each cell, left and right.</summary>
    public const double Pad = 3;

    public const double IndentPerLevel = 8;

    const double MinTextWidth = 40;

    /// <summary>
    /// Widths for the columns across the given width. Number columns take their widest cell; text columns share the rest,
    /// the first taking at least 30% when it needs it. Null when the number columns alone, or the text columns at their
    /// narrowest, cannot fit.
    /// </summary>
    public static IReadOnlyList<TableColumn>? Fit(IReadOnlyList<ReportColumn> columns, IEnumerable<IReadOnlyList<string>> cells,
        double width, double fontSize, ITextMeasure m)
    {
        var natural = NaturalWidths(columns, cells, fontSize, m);
        var numbers = columns.Select((c, i) => (c, i)).Where(x => x.c.Align == CellAlign.Right).Sum(x => natural[x.i]);
        var texts = columns.Select((c, i) => (c, i)).Where(x => x.c.Align == CellAlign.Left).Select(x => x.i).ToList();
        var available = width - numbers;
        if (available < texts.Count * MinTextWidth - 0.001) return null;

        var widths = natural.ToArray();
        var wanted = texts.Sum(i => natural[i]);
        if (texts.Count > 0)
        {
            if (wanted <= available)
            {
                widths[texts[0]] += available - wanted;
            }
            else
            {
                // Too wide: the first text column keeps at least 30% of the width (or what it needs), the rest share what is left.
                var first = Math.Min(natural[texts[0]], Math.Max(0.3 * width, MinTextWidth));
                first = Math.Min(first, available - (texts.Count - 1) * MinTextWidth);
                widths[texts[0]] = first;
                var rest = texts.Skip(1).ToList();
                var restWanted = rest.Sum(i => natural[i]);
                var restAvailable = available - first;
                foreach (var i in rest) widths[i] = Math.Max(MinTextWidth, natural[i] * restAvailable / restWanted);
                // Rounding up to the minimum may overshoot; take it back from the first column.
                var over = texts.Sum(i => widths[i]) - available;
                if (over > 0) widths[texts[0]] -= over;
                if (widths[texts[0]] < MinTextWidth - 0.001) return null;
            }
        }
        return [.. columns.Select((c, i) => new TableColumn(c.Header, widths[i],
            c.Align == CellAlign.Right ? PrintAlign.Right : PrintAlign.Left, c.Align == CellAlign.Right ? PrintFace.Number : PrintFace.Text))];
    }

    /// <summary>The width of the number columns at their widest cell: what a layout must leave for them.</summary>
    public static double NumberWidth(IReadOnlyList<ReportColumn> columns, IEnumerable<IReadOnlyList<string>> cells, double fontSize, ITextMeasure m)
    {
        var natural = NaturalWidths(columns, cells, fontSize, m);
        return columns.Select((c, i) => (c, i)).Where(x => x.c.Align == CellAlign.Right).Sum(x => natural[x.i]);
    }

    static double[] NaturalWidths(IReadOnlyList<ReportColumn> columns, IEnumerable<IReadOnlyList<string>> cells, double fontSize, ITextMeasure m)
    {
        var header = new PrintFont(PrintFace.Text, fontSize, Bold: true);
        var widths = columns.Select(c => m.Width(c.Header, header) + 2 * Pad).ToArray();
        foreach (var row in cells)
            for (var i = 0; i < columns.Count && i < row.Count; i++)
            {
                // Totals are bold, so measure cells bold: a column then fits both.
                var font = new PrintFont(columns[i].Align == CellAlign.Right ? PrintFace.Number : PrintFace.Text, fontSize, Bold: true);
                widths[i] = Math.Max(widths[i], m.Width(row[i], font) + 2 * Pad + (i == 0 ? 3 * IndentPerLevel : 0));
            }
        return widths;
    }

    /// <summary>Writes the header and rows from the page's cursor. After each page break, onNewPage runs before the header repeats.</summary>
    public static void Write(PageBuilder page, IReadOnlyList<TableColumn> columns, IEnumerable<TableRow> rows, double fontSize,
        ITextMeasure m, Action? onNewPage = null)
    {
        var headerFont = new PrintFont(PrintFace.Text, fontSize, Bold: true);
        var header = Layout(page, columns, [.. columns.Select(c => c.Header)], _ => headerFont, 0, m);
        Place(page, header, rule: false);
        page.Add(new PrintRule(page.Left, page.Y, page.Left + columns.Sum(c => c.Width), page.Y));
        page.Advance(1);

        foreach (var row in rows)
        {
            var cell = Layout(page, columns, row.Cells, i => new PrintFont(columns[i].Face, fontSize, row.Bold), row.Depth, m);
            // A row taller than a whole page is placed anyway rather than looping over empty pages.
            if (!page.Fits(cell.Height) && page.Y > page.Top + header.Height + 1.5)
            {
                page.NewPage();
                onNewPage?.Invoke();
                Place(page, header, rule: false);
                page.Add(new PrintRule(page.Left, page.Y, page.Left + columns.Sum(c => c.Width), page.Y));
                page.Advance(1);
            }
            if (row.Bold) page.Add(new PrintRule(page.Left, page.Y, page.Left + columns.Sum(c => c.Width), page.Y));
            Place(page, cell, rule: false);
        }
    }

    sealed record Block(double Height, IReadOnlyList<(double X, double DY, double Width, string Text, PrintFont Font, PrintAlign Align)> Texts);

    static Block Layout(PageBuilder page, IReadOnlyList<TableColumn> columns, IReadOnlyList<string> cells, Func<int, PrintFont> font,
        int depth, ITextMeasure m)
    {
        var texts = new List<(double, double, double, string, PrintFont, PrintAlign)>();
        var x = page.Left;
        var height = 0.0;
        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            var f = font(i);
            var indent = i == 0 ? Math.Min(depth * IndentPerLevel, 3 * IndentPerLevel) : 0;
            var boxX = x + Pad + indent;
            var boxWidth = c.Width - 2 * Pad - indent;
            var text = i < cells.Count ? cells[i] : "";
            IReadOnlyList<string> lines = c.Face == PrintFace.Number ? [text] : TextWrap.Lines(text, boxWidth, f, m);
            var lh = m.LineHeight(f);
            for (var l = 0; l < lines.Count; l++) texts.Add((boxX, 1 + l * lh, boxWidth, lines[l], f, c.Align));
            height = Math.Max(height, lines.Count * lh + 2);
            x += c.Width;
        }
        return new Block(height, texts);
    }

    static void Place(PageBuilder page, Block block, bool rule)
    {
        foreach (var (x, dy, width, text, font, align) in block.Texts)
            if (text.Length > 0) page.Add(new PrintText(x, page.Y + dy, width, text, font, align));
        if (rule) page.Add(new PrintRule(page.Left, page.Y, page.Right, page.Y));
        page.Advance(block.Height);
    }
}
