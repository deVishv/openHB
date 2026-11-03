using OpenHisabKitab.Client.Model.Printing;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

public sealed class PageBuilderTests
{
    static readonly PrintFont Body = new(PrintFace.Text, 10);

    [Fact]
    public void Page_builder_starts_a_new_page_when_a_block_does_not_fit()
    {
        var page = new PageBuilder(PaperSize.A4Portrait, footerHeight: 20);
        Assert.Equal((36, 36, 559.28, 841.89 - 36 - 20), (page.Left, page.Top, page.Right, page.Bottom));
        Assert.Equal(page.Top, page.Y);
        Assert.Equal(1, page.PageCount);

        page.Add(new PrintText(page.Left, page.Y, 100, "first", Body));
        page.Advance(700);
        Assert.True(page.Fits(page.Bottom - page.Y));
        Assert.False(page.Fits(page.Bottom - page.Y + 0.01));

        page.NewPage();
        Assert.Equal(2, page.PageCount);
        Assert.Equal(page.Top, page.Y);
        page.Add(new PrintText(page.Left, page.Y, 100, "second", Body));
        Assert.Equal(["first"], page.Pages[0].OfType<PrintText>().Select(t => t.Text));
        Assert.Equal(["second"], page.Pages[1].OfType<PrintText>().Select(t => t.Text));
    }
}
