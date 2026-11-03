using System.Collections.Concurrent;
using PdfSharp.Drawing;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>How wide text is, in points. Layouts measure through one source, so paper and PDF break pages alike.</summary>
public interface ITextMeasure
{
    double Width(string text, PrintFont font);

    double LineHeight(PrintFont font);
}

/// <summary>Measures with PDFsharp, using the same fonts the PDF embeds.</summary>
public sealed class PdfMeasure : ITextMeasure
{
    readonly ConcurrentDictionary<PrintFont, XFont> fonts = new();
    readonly Lock gate = new();
    readonly XGraphics context;

    PdfMeasure()
    {
        PdfFonts.Install();
        context = XGraphics.CreateMeasureContext(new XSize(PaperSize.A4Portrait.Width, PaperSize.A4Portrait.Height),
            XGraphicsUnit.Point, XPageDirection.Downwards);
    }

    public static PdfMeasure Instance { get; } = new();

    internal XFont Font(PrintFont f) => fonts.GetOrAdd(f, x =>
        new XFont(PdfFonts.Family(x.Face), x.Size, x.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular));

    public double Width(string text, PrintFont font)
    {
        if (text.Length == 0) return 0;
        var f = Font(font);
        lock (gate) return context.MeasureString(text, f).Width;
    }

    public double LineHeight(PrintFont font) => Font(font).GetHeight();
}
