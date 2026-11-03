using System.Globalization;
using OpenHisabKitab.Client.Model.Screens.Reports;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>A report as printed: the company, its heading, and its lines with the screen's text.</summary>
public sealed record ReportPrintInput(string Company, string Heading, string PrintedOn, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<ReportLine> Lines);

/// <summary>
/// Prints any report screen's table: portrait when it fits, else landscape, else landscape at a smaller size with text
/// wrapping. Every page carries the column headers, the time printed, and "Page n of m".
/// </summary>
public static class ReportPrint
{
    const double BodySize = 8.5;
    const double SmallSize = 7;
    const double TextColumnRoom = 120;
    static readonly PrintFont Footer = new(PrintFace.Text, 8);

    public static PrintDocument Build(ReportPrintInput input, ITextMeasure m)
    {
        IReadOnlyList<IReadOnlyList<string>> cells = [.. input.Lines.Select(l => (IReadOnlyList<string>)[.. l.Cells.Select(c => c.Text)])];
        var textColumns = input.Columns.Count(c => c.Align == CellAlign.Left);
        bool Roomy(PaperSize paper, double size) =>
            PrintTable.NumberWidth(input.Columns, cells, size, m) + TextColumnRoom * textColumns <= paper.Width - 2 * PaperSize.Margin;
        var (paper, size) = Roomy(PaperSize.A4Portrait, BodySize) ? (PaperSize.A4Portrait, BodySize)
            : Roomy(PaperSize.A4Landscape, BodySize) ? (PaperSize.A4Landscape, BodySize)
            : (PaperSize.A4Landscape, SmallSize);

        var page = new PageBuilder(paper, m.LineHeight(Footer) + 4);
        var columns = PrintTable.Fit(input.Columns, cells, page.Width, size, m) ?? Even(input.Columns, page.Width);

        Lines(page, [input.Company], new PrintFont(PrintFace.Text, 12, Bold: true), m);
        Lines(page, [input.Heading], new PrintFont(PrintFace.Text, 11, Bold: true), m);
        page.Advance(6);
        var rows = input.Lines.Select(l => new TableRow([.. l.Cells.Select(c => c.Text)], l.Emphasis, l.Depth));
        PrintTable.Write(page, columns, rows, size, m, onNewPage: () =>
        {
            Lines(page, [input.Company + ", " + input.Heading], new PrintFont(PrintFace.Text, 9, Bold: true), m);
            page.Advance(4);
        });

        AddFooters(page, Format(Resources.Text.PrintedOn, input.PrintedOn), m);
        return page.Build(input.Heading);
    }

    /// <summary>Writes each text wrapped across the page's width, moving the cursor down.</summary>
    internal static void Lines(PageBuilder page, IEnumerable<string> texts, PrintFont font, ITextMeasure m, double? x = null, double? width = null)
    {
        var (left, w) = (x ?? page.Left, width ?? page.Width);
        foreach (var text in texts)
            foreach (var line in TextWrap.Lines(text, w, font, m))
            {
                if (line.Length > 0) page.Add(new PrintText(left, page.Y, w, line, font));
                page.Advance(m.LineHeight(font));
            }
    }

    /// <summary>The footer of every page: a left text, and "Page n of m" on the right.</summary>
    internal static void AddFooters(PageBuilder page, string left, ITextMeasure m)
    {
        var y = page.Paper.Height - PaperSize.Margin - m.LineHeight(Footer);
        var half = page.Width / 2;
        for (var i = 0; i < page.PageCount; i++)
        {
            if (left.Length > 0) page.Pages[i].Add(new PrintText(page.Left, y, half, left, Footer));
            page.Pages[i].Add(new PrintText(page.Left + half, y, half, Format(Resources.Text.PageOf, i + 1, page.PageCount), Footer, PrintAlign.Right));
        }
    }

    // Only when even the number columns do not fit landscape at 7 points: every column gets the same share.
    static IReadOnlyList<TableColumn> Even(IReadOnlyList<ReportColumn> columns, double width) =>
        [.. columns.Select(c => new TableColumn(c.Header, width / columns.Count,
            c.Align == CellAlign.Right ? PrintAlign.Right : PrintAlign.Left, c.Align == CellAlign.Right ? PrintFace.Number : PrintFace.Text))];

    static string Format(string pattern, params object[] args) => string.Format(CultureInfo.InvariantCulture, pattern, args);
}
