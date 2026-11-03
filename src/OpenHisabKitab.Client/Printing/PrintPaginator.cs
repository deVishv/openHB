using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenHisabKitab.Client.Model.Printing;

namespace OpenHisabKitab.Client.Printing;

/// <summary>
/// Draws the same pages the PDF holds, for a printer. Each text sits in its measured box and is clipped to it: WPF falls
/// back to Nirmala UI for Indic names and shapes them correctly, but its widths differ from PDFsharp's measure, so a wider
/// run is cut at its box rather than overlapping the next.
/// </summary>
public sealed class PrintPaginator(PrintDocument doc) : DocumentPaginator
{
    const double Dip = 96.0 / 72.0;

    public override bool IsPageCountValid => true;

    public override int PageCount => doc.Pages.Count;

    public override Size PageSize { get; set; } = new(doc.Paper.Width * Dip, doc.Paper.Height * Dip);

    public override IDocumentPaginatorSource? Source => null;

    public override DocumentPage GetPage(int pageNumber)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            foreach (var item in doc.Pages[pageNumber].Items) Draw(dc, item);
        var bounds = new Rect(PageSize);
        return new DocumentPage(visual, PageSize, bounds, bounds);
    }

    static void Draw(DrawingContext dc, PrintItem item)
    {
        switch (item)
        {
            case PrintText t:
                var family = new FontFamily(t.Font.Face == PrintFace.Number ? "Consolas" : "Segoe UI");
                var typeface = new Typeface(family, FontStyles.Normal, t.Font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var text = new FormattedText(t.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, t.Font.Size * Dip,
                    Brushes.Black, 1.0)
                {
                    MaxTextWidth = Math.Max(1, t.Width * Dip),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.None,
                    TextAlignment = t.Align switch
                    {
                        PrintAlign.Right => TextAlignment.Right,
                        PrintAlign.Center => TextAlignment.Center,
                        _ => TextAlignment.Left,
                    },
                };
                dc.PushClip(new RectangleGeometry(new Rect(t.X * Dip, t.Y * Dip, Math.Max(1, t.Width * Dip), text.Height)));
                dc.DrawText(text, new Point(t.X * Dip, t.Y * Dip));
                dc.Pop();
                break;
            case PrintRule r:
                dc.DrawLine(new Pen(Brushes.Black, r.Thickness * Dip), new Point(r.X1 * Dip, r.Y1 * Dip), new Point(r.X2 * Dip, r.Y2 * Dip));
                break;
            case PrintImage i:
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(i.Png);
                image.EndInit();
                image.Freeze();
                dc.DrawImage(image, new Rect(i.X * Dip, i.Y * Dip, i.Width * Dip, i.Height * Dip));
                break;
        }
    }
}
