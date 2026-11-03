namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>
/// Which text a PDF can show. Segoe UI has no Indic glyphs and PDFsharp does not shape Indic scripts, so a Hindi or
/// Gujarati name would print as empty boxes. PRNT refuses such a PDF instead (owner decision, 2026-11-03). Invisible
/// characters (C1 controls, zero width and direction marks) and currency signs newer than the fonts are refused too.
/// </summary>
public static class PdfText
{
    /// <summary>The first text with a character the PDF fonts cannot show, or null.</summary>
    public static string? FirstUnsupported(PrintDocument doc) =>
        doc.Pages.SelectMany(p => p.Items).OfType<PrintText>().Select(t => t.Text).FirstOrDefault(t => !t.All(Supported));

    static bool Supported(char c) => c is '\t' or '\n'
        or (>= ' ' and <= '~')     // ASCII
        or (>= ' ' and <= 'ɏ')     // Latin-1 and Latin Extended, without the C1 controls
        or (>= '‐' and <= '‧')     // dashes, quotes, bullets, ellipsis; no zero width or direction marks
        or (>= '‰' and <= '⁞')     // per mille, primes, and other visible punctuation
        or (>= '₠' and <= '₿');    // currency signs the fonts carry, including the rupee sign
}
