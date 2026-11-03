using System.Globalization;
using System.Text;
using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

/// <summary>
/// Writes a report as CSV (spec 6.3: every report exports to CSV). UTF-8 with a byte order mark so spreadsheets read ₹
/// and Indian names; CRLF line ends and RFC 4180 quoting; amounts as plain signed decimals so they stay numbers; and a
/// leading ' on text a spreadsheet would otherwise run as a formula.
/// </summary>
public static class CsvExport
{
    static readonly char[] NeedsQuotes = [',', '"', '\r', '\n'];
    static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static void Write(IReadOnlyList<ReportColumn> columns, IEnumerable<ReportLine> lines, Stream to)
    {
        using var w = new StreamWriter(to, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true) { NewLine = "\r\n" };
        w.WriteLine(string.Join(',', columns.Select(c => Text(c.Header))));
        foreach (var line in lines)
            w.WriteLine(string.Join(',', line.Cells.Select(c => c.Amount is { } m ? Plain(m) : Text(c.Text))));
    }

    static string Plain(Money m)
    {
        var abs = m.Paise < 0 ? -(decimal)m.Paise : m.Paise;
        var text = (abs / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return m.Paise < 0 ? "-" + text : text;
    }

    static string Text(string s)
    {
        if (s.Length > 0 && FormulaStarts.Contains(s[0])) s = "'" + s;
        return s.IndexOfAny(NeedsQuotes) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
    }
}
