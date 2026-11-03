using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>Writes a print document as PDF with PDFsharp's Core build (6.2.4, MIT), embedding the fonts it uses.</summary>
public static class PdfExport
{
    public static void Write(PrintDocument doc, Stream to)
    {
        var measure = PdfMeasure.Instance;
        using var pdf = new PdfDocument();
        pdf.Info.Title = doc.Title;
        foreach (var page in doc.Pages)
        {
            var p = pdf.AddPage();
            p.Width = XUnit.FromPoint(doc.Paper.Width);
            p.Height = XUnit.FromPoint(doc.Paper.Height);
            using var g = XGraphics.FromPdfPage(p);
            foreach (var item in page.Items) Draw(g, item, measure);
        }
        pdf.Save(to, closeStream: false);
    }

    /// <summary>True when PDFsharp can read the image, as a logo must be before it is stored.</summary>
    public static bool CanDraw(byte[] png)
    {
        try
        {
            using var image = XImage.FromStream(new MemoryStream(png));
            return image.PixelWidth > 0;
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or NotSupportedException
            or IndexOutOfRangeException or InvalidDataException)
        {
            return false;
        }
    }

    static void Draw(XGraphics g, PrintItem item, PdfMeasure measure)
    {
        switch (item)
        {
            case PrintText t:
                var format = t.Align switch
                {
                    PrintAlign.Right => XStringFormats.TopRight,
                    PrintAlign.Center => XStringFormats.TopCenter,
                    _ => XStringFormats.TopLeft,
                };
                var font = measure.Font(t.Font);
                g.DrawString(t.Text, font, XBrushes.Black, new XRect(t.X, t.Y, t.Width, font.GetHeight()), format);
                break;
            case PrintRule r:
                g.DrawLine(new XPen(XColors.Black, r.Thickness), r.X1, r.Y1, r.X2, r.Y2);
                break;
            case PrintImage i:
                using (var image = XImage.FromStream(new MemoryStream(i.Png))) g.DrawImage(image, i.X, i.Y, i.Width, i.Height);
                break;
        }
    }
}
