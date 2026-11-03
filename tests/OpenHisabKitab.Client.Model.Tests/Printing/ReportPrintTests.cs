using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Screens.Reports;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

public sealed class ReportPrintTests
{
    static readonly ITextMeasure M = PdfMeasure.Instance;

    static ReportColumn Text(string h) => new(h, CellAlign.Left);
    static ReportColumn Num(string h) => new(h, CellAlign.Right);

    static readonly IReadOnlyList<ReportColumn> LedgerColumns =
        [Text("Date"), Text("Particulars"), Text("Type"), Text("Number"), Num("Debit"), Num("Credit"), Num("Balance")];

    static ReportLine Row(int n) => new(0,
    [
        new($"Row {n:0000}"), new("Sales"), new("Receipt"), new($"RCT/26-27/{n:00000}"), new("100.00"), new("0.00"),
        new($"{n},00.00 Dr"),
    ], null, false);

    static PrintDocument Ledger(int rows) => ReportPrint.Build(new ReportPrintInput("A Traders",
        "Ledger, Cash, 01-04-2026 to 31-03-2027", "01-06-2026 09:30", LedgerColumns, [.. Enumerable.Range(1, rows).Select(Row)]), M);

    static IEnumerable<PrintText> Texts(PrintPage p) => p.Items.OfType<PrintText>();

    [Fact]
    public void Report_rows_all_print_once_in_order_across_pages()
    {
        var doc = Ledger(600);
        Assert.True(doc.Pages.Count > 5);
        var printed = doc.Pages.SelectMany(Texts).Select(t => t.Text).Where(t => t.StartsWith("Row ", StringComparison.Ordinal)).ToList();
        Assert.Equal(Enumerable.Range(1, 600).Select(n => $"Row {n:0000}"), printed);
        Assert.Equal("Ledger, Cash, 01-04-2026 to 31-03-2027", doc.Title);
    }

    [Fact]
    public void Column_headers_repeat_on_every_page()
    {
        var doc = Ledger(600);
        Assert.All(doc.Pages, p =>
        {
            Assert.Single(Texts(p), t => t.Text == "Particulars" && t.Font.Bold);
            Assert.Single(Texts(p), t => t.Text == "Balance" && t.Font.Bold && t.Align == PrintAlign.Right);
        });
        Assert.Contains(Texts(doc.Pages[0]), t => t.Text == "A Traders" && t.Font.Bold);
        Assert.Contains(Texts(doc.Pages[1]), t => t.Text.StartsWith("A Traders", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_numbers_read_n_of_m()
    {
        var doc = Ledger(600);
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            Assert.Single(Texts(doc.Pages[i]), t => t.Text == $"Page {i + 1} of {doc.Pages.Count}" && t.Align == PrintAlign.Right);
            Assert.Single(Texts(doc.Pages[i]), t => t.Text == "Printed 01-06-2026 09:30");
        }
    }

    [Fact]
    public void No_row_splits_across_pages()
    {
        var doc = Ledger(600);
        foreach (var n in Enumerable.Range(1, 600))
        {
            var pages = doc.Pages.Where(p => Texts(p).Any(t => t.Text == $"RCT/26-27/{n:00000}")).ToList();
            Assert.Single(pages);
            Assert.Contains(Texts(pages[0]), t => t.Text == $"Row {n:0000}");
        }
    }

    [Fact]
    public void Wide_outstanding_prints_landscape_and_trial_balance_portrait()
    {
        IReadOnlyList<ReportColumn> outs = [Text("Party"), Num("Not due"), Num("1-30"), Num("31-60"), Num("61-90"), Num("Over 90"),
            Num("On account"), Num("Total")];
        var wide = new ReportLine(0, [new("Ravi Stores"), .. Enumerable.Repeat(new ReportCell("1,23,45,678.00"), 7)], null, false);
        Assert.True(ReportPrint.Build(new ReportPrintInput("A", "Receivables", "x", outs, [wide]), M).Paper.IsLandscape);

        IReadOnlyList<ReportColumn> trlb = [Text("Ledger"), Num("Opening"), Num("Debit"), Num("Credit"), Num("Closing")];
        var row = new ReportLine(0, [new("Cash"), new("0.00"), new("12,345.60"), new("0.00"), new("12,345.60 Dr")], null, false);
        Assert.False(ReportPrint.Build(new ReportPrintInput("A", "Trial balance", "x", trlb, [row]), M).Paper.IsLandscape);
    }

    [Fact]
    public void Long_ledger_name_wraps_and_never_overlaps()
    {
        var name = string.Join(' ', Enumerable.Repeat("Very long ledger name for wrapping", 8));
        IReadOnlyList<ReportColumn> trlb = [Text("Ledger"), Num("Opening"), Num("Debit"), Num("Credit"), Num("Closing")];
        var lines = Enumerable.Range(0, 80).Select(i => new ReportLine(i % 3,
            [new(i % 5 == 0 ? name : $"Ledger {i}"), new("0.00"), new("12,345.60"), new("0.00"), new("12,345.60 Dr")], null, i % 7 == 0)).ToList();
        var doc = ReportPrint.Build(new ReportPrintInput("A Traders", "Trial balance", "x", trlb, lines), M);
        Assert.True(doc.Pages.SelectMany(Texts).Count(t => t.Text.StartsWith("Very long", StringComparison.Ordinal)) > 16);
        AssertLaidOut(doc);
    }

    [Fact]
    public void Fit_refuses_number_columns_that_overflow_even_without_text_columns()
    {
        IReadOnlyList<ReportColumn> numbers = [.. Enumerable.Range(1, 12).Select(i => Num($"Amount {i}"))];
        IReadOnlyList<IReadOnlyList<string>> cells = [[.. Enumerable.Repeat("99,99,99,999.99", 12)]];
        Assert.Null(PrintTable.Fit(numbers, cells, 523.28, 8.5, M));
        Assert.NotNull(PrintTable.Fit(numbers.Take(3).ToList(), [[.. cells[0].Take(3)]], 523.28, 8.5, M));
    }

    [Fact]
    public void Too_wide_for_any_fit_still_never_overlaps()
    {
        // Twelve crore sized number columns cannot fit landscape even at 7 points: the fallback must still keep boxes apart.
        IReadOnlyList<ReportColumn> columns = [Text("Party"), .. Enumerable.Range(1, 12).Select(i => Num($"Amount {i}"))];
        var line = new ReportLine(0, [new("Ravi Stores"), .. Enumerable.Repeat(new ReportCell("99,99,99,999.99"), 12)], null, false);
        AssertLaidOut(ReportPrint.Build(new ReportPrintInput("A", "Wide", "x", columns, [line]), M));
    }

    [Fact]
    public void Tight_columns_keep_short_text_columns_at_their_natural_width()
    {
        IReadOnlyList<ReportColumn> columns =
        [
            Num("#"), Text("Description"), Text("HSN/SAC"), Num("Qty"), Text("Unit"), Num("Rate"), Num("Discount"), Num("Taxable value"),
            Num("CGST %"), Num("CGST"), Num("SGST %"), Num("SGST"),
        ];
        IReadOnlyList<IReadOnlyList<string>> cells =
            [["1", "Fabrication and fitting at site, with a long description", "998314", "1", "", "15,000.00", "0.00", "15,000.00", "9%", "1,350.00", "9%", "1,350.00"]];
        var fit = PrintTable.Fit(columns, cells, 523.28, 8.5, M)!;
        var unit = fit.Single(c => c.Header == "Unit").Width;
        var hsn = fit.Single(c => c.Header == "HSN/SAC").Width;
        Assert.Equal(M.Width("Unit", new PrintFont(PrintFace.Text, 8.5, Bold: true)) + 2 * PrintTable.Pad, unit, 0.01);
        Assert.Equal(M.Width("HSN/SAC", new PrintFont(PrintFace.Text, 8.5, Bold: true)) + 2 * PrintTable.Pad, hsn, 0.01);
        Assert.Equal(523.28, fit.Sum(c => c.Width), 0.01);
        Assert.True(fit.Single(c => c.Header == "Description").Width > unit + hsn);
    }

    /// <summary>Every text box lies inside the margins, fits its box, and no two boxes on a page intersect.</summary>
    internal static void AssertLaidOut(PrintDocument doc)
    {
        var (left, right) = (PaperSize.Margin - 0.01, doc.Paper.Width - PaperSize.Margin + 0.01);
        var (top, bottom) = (PaperSize.Margin - 0.01, doc.Paper.Height - PaperSize.Margin + 0.01);
        foreach (var page in doc.Pages)
        {
            var boxes = Texts(page).Where(t => t.Text.Length > 0).Select(t =>
            {
                var h = M.LineHeight(t.Font);
                Assert.True(t.X >= left && t.X + t.Width <= right && t.Y >= top && t.Y + h <= bottom, $"'{t.Text}' leaves the margins");
                Assert.True(M.Width(t.Text, t.Font) <= t.Width + 0.01, $"'{t.Text}' is wider than its box");
                return (t.Text, X1: t.X, X2: t.X + t.Width, Y1: t.Y, Y2: t.Y + h);
            }).ToList();
            for (var i = 0; i < boxes.Count; i++)
                for (var j = i + 1; j < boxes.Count; j++)
                {
                    var (a, b) = (boxes[i], boxes[j]);
                    var overlap = a.X1 < b.X2 - 0.01 && b.X1 < a.X2 - 0.01 && a.Y1 < b.Y2 - 0.01 && b.Y1 < a.Y2 - 0.01;
                    Assert.False(overlap, $"'{a.Text}' overlaps '{b.Text}'");
                }
        }
    }
}
