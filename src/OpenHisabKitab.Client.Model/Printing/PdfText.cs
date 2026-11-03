namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>
/// Which text a PDF can show. Segoe UI has no Indic glyphs and PDFsharp does not shape Indic scripts, so a Hindi or
/// Gujarati name would print as empty boxes. PRNT refuses such a PDF instead (owner decision, 2026-11-03).
/// </summary>
public static class PdfText
{
    /// <summary>The first text with a character the PDF fonts cannot show, or null.</summary>
    public static string? FirstUnsupported(PrintDocument doc) =>
        doc.Pages.SelectMany(p => p.Items).OfType<PrintText>().Select(t => t.Text).FirstOrDefault(t => !t.All(Supported));

    static bool Supported(char c) => c is '\t' or '\n'
        or >= ' ' and <= 'ɏ'           // Latin
        or >= ' ' and <= '⁯'      // punctuation
        or >= '₠' and <= '⃏';     // currency, including ₹
}
