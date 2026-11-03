using System.Buffers.Binary;
using System.Globalization;
using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;
using T = OpenHisabKitab.Client.Model.Resources.Text;

namespace OpenHisabKitab.Client.Model.Printing;

/// <summary>
/// The built in layout for GST documents this business issues (spec 13): every particular Rule 46 asks of a tax
/// invoice, Rule 49 of a bill of supply, and Rule 53 of a note. Figures are the stored ones: line amounts as saved, totals
/// as their sums, and the saved round off. Nothing is recalculated.
/// </summary>
public static class InvoicePrint
{
    public const string OriginalOnly = "ORIGINAL FOR RECIPIENT";

    const double Size = 8.5;
    static readonly PrintFont Body = new(PrintFace.Text, Size);
    static readonly PrintFont BodyBold = new(PrintFace.Text, Size, Bold: true);
    static readonly PrintFont Label = new(PrintFace.Text, 8, Bold: true);
    static readonly PrintFont Heading = new(PrintFace.Text, 14, Bold: true);
    static readonly PrintFont Name = new(PrintFace.Text, 10, Bold: true);
    static readonly PrintFont Amount = new(PrintFace.Number, Size);
    static readonly PrintFont AmountBold = new(PrintFace.Number, Size, Bold: true);
    const double FooterRoom = 14;

    /// <summary>The first particular the document lacks, or null when it may print.</summary>
    public static string? Check(InvoicePrintInput input, GstData gst)
    {
        var v = input.Voucher;
        var inv = v.Invoice;
        return InvoiceParticulars.FirstMissing(new ParticularsInput(Type(v), input.Registration, Kind(inv), EveryLineTaxFree(inv),
            Date(v.Date), input.Company, input.Buyer, input.ShipTo, new Money(inv.Lines.Sum(l => l.TaxablePaise)),
            string.IsNullOrWhiteSpace(inv.OriginalNumber) ? null : inv.OriginalNumber, OptionalDate(inv.OriginalDate),
            [.. inv.Lines.Select(l => l.Entry.Description)]), gst);
    }

    /// <summary>Rule 48 asks for three copies when any line is goods, two for services.</summary>
    public static bool AnyGoods(VoucherMsg v) => v.Invoice.Lines.Any(l => !InvoiceWording.IsService(l.Entry.HsnSac));

    public static string Title(InvoicePrintInput input) =>
        InvoiceWording.Title(Type(input.Voucher), input.Registration, Kind(input.Voucher.Invoice), EveryLineTaxFree(input.Voucher.Invoice));

    /// <summary>One full set of pages per copy label, each numbered on its own.</summary>
    public static PrintDocument Build(InvoicePrintInput input, IReadOnlyList<string> copyLabels, GstData gst, ITextMeasure m)
    {
        var title = Title(input);
        var pages = copyLabels.SelectMany(label => Copy(input, title, label, gst, m).Build("").Pages).ToList();
        return new PrintDocument($"{title} {input.Voucher.DisplayNumber}", PaperSize.A4Portrait, pages);
    }

    static PageBuilder Copy(InvoicePrintInput input, string title, string copyLabel, GstData gst, ITextMeasure m)
    {
        var v = input.Voucher;
        var inv = v.Invoice;
        var isNote = Type(v) is VoucherType.CreditNote or VoucherType.DebitNote;
        var page = new PageBuilder(PaperSize.A4Portrait, FooterRoom);

        void CopyLabel()
        {
            page.Add(new PrintText(page.Left, page.Y, page.Width, copyLabel, Label, PrintAlign.Right));
            page.Advance(m.LineHeight(Label));
        }
        void Continued()
        {
            CopyLabel();
            Write(page, page.Left, page.Width, [(F(T.PrintContinued, title, v.DisplayNumber, Show(v.Date)), BodyBold)], m);
            page.Advance(4);
        }

        CopyLabel();
        Centred(page, title, Heading, m);
        if (input.Registration == GstRegistration.Composition && !isNote) Centred(page, InvoiceWording.CompositionLine, Body, m);
        if (v.Cancelled) Centred(page, T.PrintCancelled, Heading, m);
        page.Advance(6);

        // The supplier on the left (Rule 46(a)); the document's own particulars on the right.
        var top = page.Y;
        var leftWidth = page.Width * 0.58 - 6;
        var rightX = page.Left + page.Width * 0.58;
        var y = top;
        if (input.Logo is { } logo && LogoSize(logo) is var (w, h))
        {
            page.Add(new PrintImage(page.Left, y, w, h, logo));
            y += h + 4;
        }
        y = WriteAt(page, page.Left, y, leftWidth, Party(input.Company, gst, Name), m);
        List<(string, PrintFont)> details =
        [
            (F(isNote ? T.PrintNoteNo : T.PrintInvoiceNo, v.DisplayNumber), BodyBold),
            (F(T.PrintDate, Show(v.Date)), Body),
            (F(T.PrintPlaceOfSupply, StateText(inv.PlaceOfSupply, gst)), Body),
            (T.PrintReverseChargeNo, Body),
        ];
        if (isNote) details.Add((F(T.PrintOriginalInvoice, inv.OriginalNumber, Show(inv.OriginalDate)), Body));
        if (inv.PortCode.Length > 0) details.Add((F(T.PrintPortCode, inv.PortCode), Body));
        if (inv.ShippingBillNumber.Length > 0) details.Add((F(T.PrintShippingBill, inv.ShippingBillNumber, Show(inv.ShippingBillDate)), Body));
        var yRight = WriteAt(page, rightX, top, page.Right - rightX, details, m);
        page.Advance(Math.Max(y, yRight) - page.Y + 4);
        Rule(page);

        // The buyer (Rule 46(d), (e)) and, beside it, where goods go (Rule 46(o)).
        top = page.Y + 2;
        var half = page.Width / 2;
        y = WriteAt(page, page.Left, top, half - 6, [(T.PrintBillTo, Label), .. Party(input.Buyer, gst, BodyBold)], m);
        if (input.ShipTo is { } ship)
        {
            List<(string, PrintFont)> shipLines = [(T.PrintShipTo, Label), (ship.Name, BodyBold), .. Lines(ship.Address, Body)];
            if (ship.State is { } st) shipLines.Add((F(T.PrintState, StateText(st.Value, gst)), Body));
            if (ship.Country is { } country) shipLines.Add((F(T.PrintCountry, country), Body));
            y = Math.Max(y, WriteAt(page, page.Left + half, top, half, shipLines, m));
        }
        page.Advance(y - page.Y + 4);
        if (InvoiceWording.Endorsement(Kind(inv)) is { } endorsement)
        {
            Write(page, page.Left, page.Width, [(endorsement, BodyBold)], m);
            page.Advance(4);
        }

        // The lines (Rule 46(g) to (m)): HSN, description, quantity and unit are always shown; price and discount by setting.
        var tax = TaxColumns(input, title, gst);
        var (columns, size) = Columns(input, tax, page.Width, m);
        var rows = inv.Lines.Select((l, i) => new TableRow(Cells(input, l, i, tax)));
        PrintTable.Write(page, columns, rows, size, m, Continued);

        // Totals, the amount in words, then the bank details, terms, and signature (Rule 46(q)), together on the last page.
        List<(string Label, Money Amount, bool Bold)> totals = [(T.PrintColTaxable, Sum(inv, l => l.TaxablePaise), false)];
        foreach (var (name, amount) in new[]
        {
            (T.PrintTaxCgst, Sum(inv, l => l.CgstPaise)), (T.PrintTaxSgst, Sum(inv, l => l.SgstPaise)), (T.PrintTaxUtgst, Sum(inv, l => l.UtgstPaise)),
            (T.PrintTaxIgst, Sum(inv, l => l.IgstPaise)), (T.PrintTaxCess, Sum(inv, l => l.CessPaise)),
        })
            if (amount != Money.Zero) totals.Add((name, amount, false));
        if (inv.RoundOffPaise != 0) totals.Add((T.PrintRoundOff, new Money(inv.RoundOffPaise), false));
        var total = Money.Sum(totals.Select(x => x.Amount));
        totals.Add((T.PrintTotal, total, true));
        var words = F(T.PrintAmountInWords, AmountInWords.Rupees(total));

        var leftFooter = new List<(string, PrintFont)>();
        if (input.BankDetails.Trim().Length > 0) leftFooter.AddRange([(T.PrintBankDetails, Label), .. Lines(input.BankDetails, Body)]);
        if (input.Terms.Trim().Length > 0) leftFooter.AddRange([(T.PrintTerms, Label), .. Lines(input.Terms, Body)]);
        var footerWidth = page.Width * 0.6 - 6;
        var signX = page.Left + page.Width * 0.6;
        var lh = m.LineHeight(Body);
        var need = 4 + totals.Count * lh + 4 + Height(words, page.Width, BodyBold, m) + 8
            + Math.Max(leftFooter.Sum(x => Height(x.Item1, footerWidth, x.Item2, m)), 2 * lh + 36);
        if (!page.Fits(need))
        {
            page.NewPage();
            Continued();
        }
        page.Advance(4);
        var amountX = page.Right - 100;
        foreach (var (label, amount, bold) in totals)
        {
            if (bold) page.Add(new PrintRule(amountX - 120, page.Y, page.Right, page.Y));
            page.Add(new PrintText(amountX - 120 + PrintTable.Pad, page.Y, 120 - 2 * PrintTable.Pad, label, bold ? BodyBold : Body));
            page.Add(new PrintText(amountX + PrintTable.Pad, page.Y, 100 - 2 * PrintTable.Pad, Rupees(amount), bold ? AmountBold : Amount, PrintAlign.Right));
            page.Advance(lh);
        }
        page.Advance(4);
        Write(page, page.Left, page.Width, [(words, BodyBold)], m);
        page.Advance(8);
        var footerTop = page.Y;
        var yLeft = WriteAt(page, page.Left, footerTop, footerWidth, leftFooter, m);
        page.Add(new PrintText(signX, footerTop, page.Right - signX, F(T.PrintFor, input.Company.Name), BodyBold, PrintAlign.Right));
        page.Add(new PrintText(signX, footerTop + lh + 36, page.Right - signX, input.Signatory, Body, PrintAlign.Right));
        page.Advance(Math.Max(yLeft, footerTop + 2 * lh + 36) - page.Y);

        ReportPrint.AddFooters(page, "", m);
        return page;
    }

    sealed record TaxSet(bool Cgst, bool Sgst, bool Utgst, bool Igst, bool Cess);

    // Columns follow the stored amounts, never the company's current state; only an all-zero document (as under LUT)
    // takes the supply type from the saved company state and the place of supply. Bills of supply show no tax.
    static TaxSet TaxColumns(InvoicePrintInput input, string title, GstData gst)
    {
        var lines = input.Voucher.Invoice.Lines;
        var cess = lines.Any(l => l.CessPaise != 0);
        if (input.Registration != GstRegistration.Regular || title == "Bill of supply") return new TaxSet(false, false, false, false, false);
        var set = new TaxSet(lines.Any(l => l.CgstPaise != 0), lines.Any(l => l.SgstPaise != 0), lines.Any(l => l.UtgstPaise != 0),
            lines.Any(l => l.IgstPaise != 0), cess);
        if (set.Cgst || set.Sgst || set.Utgst || set.Igst) return set;
        var kind = Kind(input.Voucher.Invoice);
        TaxSplit split;
        try
        {
            split = GstRules.Split(input.Company.State, new StateCode(input.Voucher.Invoice.PlaceOfSupply),
                kind is not (InvoiceKind.Regular or InvoiceKind.DeemedExport), gst);
        }
        catch (Exception e) when (e is ArgumentException or KeyNotFoundException) { split = TaxSplit.Igst; }
        return split switch
        {
            TaxSplit.CgstSgst => set with { Cgst = true, Sgst = true },
            TaxSplit.CgstUtgst => set with { Cgst = true, Utgst = true },
            _ => set with { Igst = true },
        };
    }

    static (IReadOnlyList<TableColumn>, double) Columns(InvoicePrintInput input, TaxSet tax, double width, ITextMeasure m)
    {
        var columns = new List<ReportColumn>
        {
            new(T.PrintColNo, CellAlign.Right), new(T.PrintColDescription, CellAlign.Left), new(T.PrintColHsn, CellAlign.Left),
            new(T.PrintColQty, CellAlign.Right), new(T.PrintColUnit, CellAlign.Left),
        };
        if (input.ShowPrice) columns.Add(new(T.PrintColRate, CellAlign.Right));
        if (input.ShowDiscount) columns.Add(new(T.PrintColDiscount, CellAlign.Right));
        columns.Add(new(T.PrintColTaxable, CellAlign.Right));
        foreach (var (shown, name) in new[] { (tax.Cgst, T.PrintTaxCgst), (tax.Sgst, T.PrintTaxSgst), (tax.Utgst, T.PrintTaxUtgst), (tax.Igst, T.PrintTaxIgst), (tax.Cess, T.PrintTaxCess) })
            if (shown) columns.AddRange([new(F(T.PrintColTaxRate, name), CellAlign.Right), new(name, CellAlign.Right)]);
        var cells = input.Voucher.Invoice.Lines.Select((l, i) => Cells(input, l, i, tax)).ToList();
        foreach (var size in new[] { Size, 7.5, 6.5, 5.5, 4.5 })
            if (PrintTable.Fit(columns, cells, width, size, m) is { } fit) return (fit, size);
        return ([.. columns.Select(c => new TableColumn(c.Header, width / columns.Count,
            c.Align == CellAlign.Right ? PrintAlign.Right : PrintAlign.Left, c.Align == CellAlign.Right ? PrintFace.Number : PrintFace.Text))], 6.5);
    }

    static IReadOnlyList<string> Cells(InvoicePrintInput input, StoredInvoiceLineMsg l, int i, TaxSet tax)
    {
        var e = l.Entry;
        var cells = new List<string>
        {
            (i + 1).ToString(CultureInfo.InvariantCulture), e.Description, e.HsnSac, Quantity(e.QtyThousandths), input.Units.ElementAtOrDefault(i) ?? "",
        };
        if (input.ShowPrice) cells.Add(Rupees(new Money(e.UnitPricePaise)));
        if (input.ShowDiscount) cells.Add(Rupees(new Money(e.DiscountPaise)));
        cells.Add(Rupees(new Money(l.TaxablePaise)));
        if (tax.Cgst) cells.AddRange([Percent(e.RateBp / 200m), Rupees(new Money(l.CgstPaise))]);
        if (tax.Sgst) cells.AddRange([Percent(e.RateBp / 200m), Rupees(new Money(l.SgstPaise))]);
        if (tax.Utgst) cells.AddRange([Percent(e.RateBp / 200m), Rupees(new Money(l.UtgstPaise))]);
        if (tax.Igst) cells.AddRange([Percent(e.RateBp / 100m), Rupees(new Money(l.IgstPaise))]);
        if (tax.Cess) cells.AddRange([Percent(e.CessBp / 100m), Rupees(new Money(l.CessPaise))]);
        return cells;
    }

    static List<(string, PrintFont)> Party(DocumentParty p, GstData gst, PrintFont nameFont)
    {
        List<(string, PrintFont)> lines = [(p.Name, nameFont), .. Lines(p.Address, Body)];
        if (p.Gstin is { } g) lines.Add((F(T.PrintGstin, g), Body));
        lines.Add((F(T.PrintState, StateText(p.State.Value, gst)), Body));
        return lines;
    }

    static IEnumerable<(string, PrintFont)> Lines(string text, PrintFont font) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(l => l.Trim().Length > 0).Select(l => (l.Trim(), font));

    /// <summary>Writes wrapped lines at a position without moving the page cursor; returns the Y below them.</summary>
    static double WriteAt(PageBuilder page, double x, double y, double width, IEnumerable<(string Text, PrintFont Font)> lines, ITextMeasure m)
    {
        foreach (var (text, font) in lines)
            foreach (var line in TextWrap.Lines(text, width, font, m))
            {
                if (line.Length > 0) page.Add(new PrintText(x, y, width, line, font));
                y += m.LineHeight(font);
            }
        return y;
    }

    static void Write(PageBuilder page, double x, double width, IEnumerable<(string Text, PrintFont Font)> lines, ITextMeasure m) =>
        page.Advance(WriteAt(page, x, page.Y, width, lines, m) - page.Y);

    static void Centred(PageBuilder page, string text, PrintFont font, ITextMeasure m)
    {
        foreach (var line in TextWrap.Lines(text, page.Width, font, m))
        {
            page.Add(new PrintText(page.Left, page.Y, page.Width, line, font, PrintAlign.Center));
            page.Advance(m.LineHeight(font));
        }
    }

    static double Height(string text, double width, PrintFont font, ITextMeasure m) => TextWrap.Lines(text, width, font, m).Count * m.LineHeight(font);

    static void Rule(PageBuilder page)
    {
        page.Add(new PrintRule(page.Left, page.Y, page.Right, page.Y));
        page.Advance(1);
    }

    /// <summary>The logo's printed size: at most 120 by 60 points, keeping its shape. Null when its header cannot be read.</summary>
    static (double, double)? LogoSize(byte[] png)
    {
        if (png.Length < 24) return null;
        double w = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), h = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (w <= 0 || h <= 0) return null;
        var scale = Math.Min(120 / w, 60 / h);
        return (w * scale, h * scale);
    }

    static Money Sum(InvoiceHeaderMsg inv, Func<StoredInvoiceLineMsg, long> amount) => new(inv.Lines.Sum(amount));

    static bool EveryLineTaxFree(InvoiceHeaderMsg inv) => inv.Lines.All(l => l.Entry.RateBp == 0 && l.Entry.CessBp == 0);

    static VoucherType Type(VoucherMsg v) => Enum.Parse<VoucherType>(v.Type);

    static InvoiceKind Kind(InvoiceHeaderMsg inv) => Enum.TryParse<InvoiceKind>(inv.Kind, out var k) ? k : InvoiceKind.Regular;

    static string StateText(string code, GstData gst) =>
        code.Length == 2 && gst.States.FirstOrDefault(s => s.Code.Value == code) is { } s ? $"{s.Name} ({code})" : code;

    static string Rupees(Money m) => IndianNumberFormat.Format(m, NegativeStyle.Minus);

    static string Quantity(long thousandths) => (thousandths / 1000m).ToString("0.###", CultureInfo.InvariantCulture);

    static string Percent(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "%";

    /// <summary>Documents always show dd-mm-yyyy, whatever the screen's date setting.</summary>
    static string Show(string iso) => iso.Length == 0 ? "" : Date(iso).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    static DateOnly Date(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly? OptionalDate(string iso) => iso.Length == 0 ? null : Date(iso);

    static string F(string pattern, params object[] args) => string.Format(CultureInfo.InvariantCulture, pattern, args);
}
