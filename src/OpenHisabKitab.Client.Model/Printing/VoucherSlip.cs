using System.Globalization;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using T = OpenHisabKitab.Client.Model.Resources.Text;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>A voucher slip as printed: its title, the company, the stored postings as Dr and Cr lines, and who signs.</summary>
public sealed record SlipInput(VoucherMsg Voucher, string Title, DocumentParty Company,
    IReadOnlyList<(string Ledger, Money Debit, Money Credit)> Lines, string? SupplierDocument, bool ReceiverSigns);

/// <summary>
/// The plain slip for vouchers that are not GST documents this business issues: purchases and received notes, receipts,
/// payments, journals, and contras. One copy, signed by whoever prepares, checks, and approves it.
/// </summary>
public static class VoucherSlip
{
    const double Size = 8.5;
    static readonly PrintFont Body = new(PrintFace.Text, Size);
    static readonly PrintFont BodyBold = new(PrintFace.Text, Size, Bold: true);
    static readonly PrintFont Name = new(PrintFace.Text, 12, Bold: true);
    static readonly PrintFont Heading = new(PrintFace.Text, 14, Bold: true);
    const double SignatureRoom = 40;

    /// <summary>The slip's lines from the stored postings, in stored order: positive amounts are debits.</summary>
    public static IReadOnlyList<(string, Money, Money)> Lines(VoucherMsg v, Func<int, string> ledgerName) =>
        [.. v.Postings.Select(p => (ledgerName(p.Ledger), p.Paise > 0 ? new Money(p.Paise) : Money.Zero, p.Paise < 0 ? new Money(-p.Paise) : Money.Zero))];

    public static PrintDocument Build(SlipInput input, ITextMeasure m)
    {
        var v = input.Voucher;
        var page = new PageBuilder(PaperSize.A4Portrait, 14);
        ReportPrint.Lines(page, [input.Company.Name], Name, m);
        ReportPrint.Lines(page, input.Company.Address.Split('\n').Where(l => l.Trim().Length > 0), Body, m);
        page.Advance(6);
        Centred(page, input.Title, Heading, m);
        if (v.Cancelled) Centred(page, T.PrintCancelled, Heading, m);
        page.Advance(4);
        List<string> details = [F(T.PrintVoucherNo, v.DisplayNumber), F(T.PrintDate, Show(v.Date))];
        if (input.SupplierDocument is { } doc) details.Add(doc);
        ReportPrint.Lines(page, details, Body, m);
        page.Advance(6);

        var debit = Money.Sum(input.Lines.Select(l => l.Debit));
        var credit = Money.Sum(input.Lines.Select(l => l.Credit));
        var amountWidth = 110;
        IReadOnlyList<TableColumn> columns =
        [
            new(T.PrintColLedger, page.Width - 2 * amountWidth, PrintAlign.Left, PrintFace.Text),
            new(T.PrintColDebit, amountWidth, PrintAlign.Right, PrintFace.Number),
            new(T.PrintColCredit, amountWidth, PrintAlign.Right, PrintFace.Number),
        ];
        var rows = input.Lines.Select(l => new TableRow([l.Ledger, Amount(l.Debit), Amount(l.Credit)]))
            .Append(new TableRow([T.PrintTotal, Amount(debit), Amount(credit)], Bold: true));
        PrintTable.Write(page, columns, rows, Size, m);

        page.Advance(6);
        List<(string, PrintFont)> after = [];
        if (v.Narration.Trim().Length > 0) after.Add((F(T.PrintNarration, v.Narration.Trim()), Body));
        after.Add((F(T.PrintAmountInWords, AmountInWords.Rupees(debit)), BodyBold));
        var height = after.Sum(a => TextWrap.Lines(a.Item1, page.Width, a.Item2, m).Count * m.LineHeight(a.Item2)) + 12 + SignatureRoom + m.LineHeight(Body);
        if (!page.Fits(height)) page.NewPage();
        foreach (var (text, font) in after) ReportPrint.Lines(page, [text], font, m);

        // Signature boxes: a line to sign on, and under it who signs.
        page.Advance(SignatureRoom);
        List<string> signers = [T.PrintPreparedBy, T.PrintCheckedBy, T.PrintApprovedBy];
        if (input.ReceiverSigns) signers.Add(T.PrintReceiverSigns);
        var box = page.Width / signers.Count;
        for (var i = 0; i < signers.Count; i++)
        {
            var x = page.Left + i * box;
            page.Add(new PrintRule(x + 6, page.Y, x + box - 6, page.Y));
            page.Add(new PrintText(x + 6, page.Y + 2, box - 12, signers[i], Body, PrintAlign.Center));
        }
        page.Advance(2 + m.LineHeight(Body));

        ReportPrint.AddFooters(page, "", m);
        return page.Build($"{input.Title} {v.DisplayNumber}");
    }

    static void Centred(PageBuilder page, string text, PrintFont font, ITextMeasure m)
    {
        foreach (var line in TextWrap.Lines(text, page.Width, font, m))
        {
            page.Add(new PrintText(page.Left, page.Y, page.Width, line, font, PrintAlign.Center));
            page.Advance(m.LineHeight(font));
        }
    }

    static string Amount(Money m) => IndianNumberFormat.Format(m, NegativeStyle.Minus);

    /// <summary>Slips always show dd-mm-yyyy, whatever the screen's date setting.</summary>
    internal static string Show(string iso) => iso.Length == 0 ? ""
        : DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    static string F(string pattern, params object[] args) => string.Format(CultureInfo.InvariantCulture, pattern, args);
}
