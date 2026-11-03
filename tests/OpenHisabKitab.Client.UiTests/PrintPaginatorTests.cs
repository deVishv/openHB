using System.Printing;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Printing;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>The WPF paper renderer. No desktop needed: WPF text and print tickets work on any STA thread.</summary>
public sealed class PrintPaginatorTests
{
    static void OnSta(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new AggregateException(failure);
    }

    [Fact]
    public void Paper_text_is_clipped_at_its_box_never_wrapped_away() => OnSta(() =>
    {
        var font = new PrintFont(PrintFace.Number, 8.5, Bold: true);
        var width = PdfMeasure.Instance.Width("12,34,567.89", font);
        foreach (var box in new[] { width, width - 1 })
        {
            var t = new PrintText(100, 200, box, "12,34,567.89", font, PrintAlign.Right);
            var (text, origin, clip) = PrintPaginator.Layout(t);
            Assert.Equal("12,34,567.89", text.Text);
            // One line, the whole run: nothing is wrapped onto a hidden second line.
            Assert.True(text.MaxLineCount >= 1);
            Assert.True(Math.Abs(text.Height - new System.Windows.Media.FormattedText("1", System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new System.Windows.Media.Typeface("Consolas"), 8.5 * 96 / 72,
                System.Windows.Media.Brushes.Black, 1.0).Height) < 2);
            // Right aligned: the run ends at the box's right edge, and only the clip limits what shows.
            Assert.Equal((100 + box) * 96 / 72, origin.X + text.WidthIncludingTrailingWhitespace, 0.01);
            var pad = PrintTable.Pad * 96 / 72;
            Assert.Equal(100 * 96.0 / 72 - pad, clip.X, 0.001);
            Assert.Equal(200 * 96.0 / 72, clip.Y, 0.001);
            Assert.Equal(box * 96 / 72 + 2 * pad, clip.Width, 0.001);
            Assert.Equal(text.Height, clip.Height, 0.001);
        }
    });

    [Fact]
    public void Print_ticket_is_a4_in_the_document_orientation() => OnSta(() =>
    {
        foreach (var paper in new[] { PaperSize.A4Portrait, PaperSize.A4Landscape })
        {
            var ticket = PrintPaginator.Ticket(new PrintDocument("t", paper, []));
            Assert.Equal(PageMediaSizeName.ISOA4, ticket.PageMediaSize!.PageMediaSizeName);
            Assert.Equal(paper.IsLandscape ? PageOrientation.Landscape : PageOrientation.Portrait, ticket.PageOrientation);
        }
    });
}
