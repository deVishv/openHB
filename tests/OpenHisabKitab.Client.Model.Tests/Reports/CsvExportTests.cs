using System.Text;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class CsvExportTests
{
    static string Write(IReadOnlyList<ReportColumn> columns, params ReportLine[] lines)
    {
        using var stream = new MemoryStream();
        CsvExport.Write(columns, lines, stream);
        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    static ReportLine Line(params ReportCell[] cells) => new(0, cells, null, false);

    [Fact]
    public void Csv_has_bom_crlf_quotes_and_plain_amounts()
    {
        var text = Write([new("Party", CellAlign.Left), new("Amount", CellAlign.Right)],
            Line(new ReportCell("Gupta, Pune"), new ReportCell("(12,345.60)", new Money(-1234560))),
            Line(new ReportCell("=SUM(A1)"), new ReportCell("1,00,000.00", new Money(10000000))),
            Line(new ReportCell("Say \"hi\""), new ReportCell("")));
        Assert.Equal(
            "Party,Amount\r\n\"Gupta, Pune\",-12345.60\r\n'=SUM(A1),100000.00\r\n\"Say \"\"hi\"\"\",\r\n", text);
    }

    [Theory]
    [InlineData("+91 98", "'+91 98")]
    [InlineData("-5 discount", "'-5 discount")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("Rent apr", "Rent apr")]
    [InlineData("₹ नमस्ते", "₹ नमस्ते")]
    public void Text_cells_that_a_spreadsheet_would_run_get_a_quote_mark(string cell, string written) =>
        Assert.Equal($"A\r\n{written}\r\n", Write([new("A", CellAlign.Left)], Line(new ReportCell(cell))));

    [Fact]
    public void Line_breaks_in_text_are_quoted()
    {
        var text = Write([new("A", CellAlign.Left)], Line(new ReportCell("two\r\nlines")));
        Assert.Equal("A\r\n\"two\r\nlines\"\r\n", text);
    }
}
