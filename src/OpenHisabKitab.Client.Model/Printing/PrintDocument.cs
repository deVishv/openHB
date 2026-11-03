namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>Text is Segoe UI; numbers are Consolas, so columns of amounts line up (spec 6.6).</summary>
public enum PrintFace { Text, Number }

public sealed record PrintFont(PrintFace Face, double Size, bool Bold = false);

public enum PrintAlign { Left, Right, Center }

/// <summary>
/// One mark on a printed page, in points from the top left corner. Layouts fill these; PDFsharp draws them for PDF and
/// a WPF paginator draws them for paper, so both show the same pages.
/// </summary>
public abstract record PrintItem;

/// <summary>One line of text in its box: X and Width give the box, Y is the top of the line. Layouts wrap first.</summary>
public sealed record PrintText(double X, double Y, double Width, string Text, PrintFont Font, PrintAlign Align = PrintAlign.Left) : PrintItem;

public sealed record PrintRule(double X1, double Y1, double X2, double Y2, double Thickness = 0.5) : PrintItem;

public sealed record PrintImage(double X, double Y, double Width, double Height, byte[] Png) : PrintItem;

public sealed record PrintPage(IReadOnlyList<PrintItem> Items);

/// <summary>A4, in points. Every layout keeps the same margin on each side.</summary>
public sealed record PaperSize(double Width, double Height)
{
    public static PaperSize A4Portrait { get; } = new(595.28, 841.89);
    public static PaperSize A4Landscape { get; } = new(841.89, 595.28);
    public const double Margin = 36;

    public bool IsLandscape => Width > Height;
}

/// <summary>A document ready to print or save: its title (the PDF title and the print job's name), paper, and pages.</summary>
public sealed record PrintDocument(string Title, PaperSize Paper, IReadOnlyList<PrintPage> Pages);

/// <summary>Paper goes to a printer; PDF is saved to a file.</summary>
public enum PrintTarget { Paper, Pdf }

/// <summary>What a screen gives PRNT: a document and its PDF file name, or the one line reason it cannot print.</summary>
public sealed record PrintResult(PrintDocument? Document, string FileName, string? Refusal)
{
    public static PrintResult Refused(string reason) => new(null, "", reason);
}

/// <summary>A screen PRNT can print. It prints what is stored, never what is only typed (Plan 6b).</summary>
public interface IPrintable
{
    Task<PrintResult> PrintAsync(PrintTarget target);
}
