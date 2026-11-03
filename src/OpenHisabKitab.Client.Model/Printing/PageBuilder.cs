namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>
/// Places blocks top down inside the margins. A layout asks whether a block fits below the cursor and starts a new page
/// when it does not; the footer band stays free and is filled once the page count is known.
/// </summary>
public sealed class PageBuilder
{
    readonly List<List<PrintItem>> pages = [[]];

    public PageBuilder(PaperSize paper, double footerHeight)
    {
        Paper = paper;
        Left = PaperSize.Margin;
        Top = PaperSize.Margin;
        Right = paper.Width - PaperSize.Margin;
        Bottom = paper.Height - PaperSize.Margin - footerHeight;
        Y = Top;
    }

    public PaperSize Paper { get; }
    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }
    public double Width => Right - Left;

    /// <summary>The cursor: where the next block starts.</summary>
    public double Y { get; private set; }

    public int PageCount => pages.Count;

    public IReadOnlyList<List<PrintItem>> Pages => pages;

    public bool Fits(double height) => Y + height <= Bottom + 0.001;

    public void NewPage()
    {
        pages.Add([]);
        Y = Top;
    }

    public void Add(PrintItem item) => pages[^1].Add(item);

    public void Advance(double height) => Y += height;

    public PrintDocument Build(string title) => new(title, Paper, [.. pages.Select(p => new PrintPage(p))]);
}
