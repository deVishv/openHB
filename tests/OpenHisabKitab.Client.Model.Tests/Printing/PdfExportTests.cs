using System.Text;
using OpenHisabKitab.Client.Model.Printing;
using PdfSharp.Pdf.IO;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

public sealed class PdfExportTests
{
    /// <summary>A real 2 by 2 PNG.</summary>
    internal static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    static readonly PrintFont Body = new(PrintFace.Text, 10);
    static readonly PrintFont Amount = new(PrintFace.Number, 10, Bold: true);

    static PrintDocument Doc(PaperSize paper, int pages = 2) => new("Trial balance", paper,
        [.. Enumerable.Range(1, pages).Select(n => new PrintPage(
        [
            new PrintText(36, 36, 300, $"Page {n} ₹ 1,00,000.00", Body),
            new PrintText(400, 36, 100, "12,345.60", Amount, PrintAlign.Right),
            new PrintRule(36, 60, 559, 60),
            new PrintImage(36, 70, 20, 20, TinyPng),
        ]))]);

    static byte[] Pdf(PrintDocument doc)
    {
        using var ms = new MemoryStream();
        PdfExport.Write(doc, ms);
        return ms.ToArray();
    }

    [Fact]
    public void Pdf_has_one_page_per_print_page_and_the_title()
    {
        var bytes = Pdf(Doc(PaperSize.A4Portrait));
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        using var read = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(2, read.PageCount);
        Assert.Equal("Trial balance", read.Info.Title);
        Assert.Equal(595.28, read.Pages[0].Width.Point, 0.01);
        Assert.Equal(841.89, read.Pages[0].Height.Point, 0.01);
    }

    [Fact]
    public void Pdf_embeds_segoe_and_consolas()
    {
        var text = Encoding.Latin1.GetString(Pdf(Doc(PaperSize.A4Portrait, 1)));
        Assert.Contains("+Segoe#20UI", text, StringComparison.Ordinal);
        Assert.Contains("+Consolas", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Landscape_pages_are_wide()
    {
        using var read = PdfReader.Open(new MemoryStream(Pdf(Doc(PaperSize.A4Landscape, 1))), PdfDocumentOpenMode.Import);
        Assert.Equal(841.89, read.Pages[0].Width.Point, 0.01);
        Assert.Equal(595.28, read.Pages[0].Height.Point, 0.01);
    }

    [Fact]
    public void Png_draws_and_other_bytes_are_refused()
    {
        Assert.True(PdfExport.CanDraw(TinyPng));
        Assert.False(PdfExport.CanDraw("hello"u8.ToArray()));
        Assert.False(PdfExport.CanDraw([]));
    }

    [Fact]
    public void Rupee_sign_and_latin_are_supported_devanagari_is_not()
    {
        PrintDocument One(string text) => new("t", PaperSize.A4Portrait, [new PrintPage([new PrintText(0, 0, 100, text, Body)])]);
        Assert.Null(PdfText.FirstUnsupported(One("Total ₹ 1,00,000.00 é – “quoted”\tTab\nLine")));
        Assert.Equal("राम Traders", PdfText.FirstUnsupported(One("राम Traders")));
        Assert.Equal("ગુજરાત", PdfText.FirstUnsupported(One("ગુજરાત")));
        // Invisible and control characters, and currency signs newer than the fonts, would also print wrongly.
        Assert.Equal("Ravi​Stores", PdfText.FirstUnsupported(One("Ravi​Stores")));
        Assert.Equal("A‮Traders", PdfText.FirstUnsupported(One("A‮Traders")));
        Assert.Equal("Bad\u0085", PdfText.FirstUnsupported(One("Bad\u0085")));
        Assert.Equal("Som ⃀", PdfText.FirstUnsupported(One("Som ⃀")));
        Assert.Null(PdfText.FirstUnsupported(One("€ £ ¥ ₹ – — “ ” …")));
    }

    [Fact]
    public async Task Fonts_install_once_under_parallel_use()
    {
        var doc = Doc(PaperSize.A4Portrait, 1);
        var sizes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => Pdf(doc).Length)));
        Assert.All(sizes, n => Assert.True(n > 1000));
        Assert.True(PdfMeasure.Instance.Width("Tax invoice", Body) > 40);
    }
}
