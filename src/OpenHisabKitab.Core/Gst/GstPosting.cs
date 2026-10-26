using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Vouchers;

namespace OpenHisabKitab.Core.Gst;

/// <summary>Outward documents are issued by this business; inward documents are received from suppliers.</summary>
public enum TradeDirection { Outward, Inward }

/// <summary>A credit note reduces the original document and a debit note increases it.</summary>
public enum GstDocumentKind { Invoice, CreditNote, DebitNote }

public sealed record TaxLedgers(LedgerId Cgst, LedgerId Sgst, LedgerId Utgst, LedgerId Igst, LedgerId Cess);

public sealed record GstPostingAccounts(LedgerId Party, LedgerId RoundOff, TaxLedgers Output, TaxLedgers Input)
{
    public static GstPostingAccounts FromChart(ChartOfAccounts chart, LedgerId party) => new(
        party,
        chart.Required(RequiredLedger.RoundOff),
        new TaxLedgers(
            chart.Required(RequiredLedger.OutputCgst), chart.Required(RequiredLedger.OutputSgst),
            chart.Required(RequiredLedger.OutputUtgst), chart.Required(RequiredLedger.OutputIgst),
            chart.Required(RequiredLedger.OutputCess)),
        new TaxLedgers(
            chart.Required(RequiredLedger.InputCgst), chart.Required(RequiredLedger.InputSgst),
            chart.Required(RequiredLedger.InputUtgst), chart.Required(RequiredLedger.InputIgst),
            chart.Required(RequiredLedger.InputCess)));
}

/// <summary>Turns a calculated GST document into balanced posting lines.</summary>
public static class GstPosting
{
    public static IReadOnlyList<PostingLine> Post(
        InvoiceResult invoice,
        IReadOnlyList<LedgerId> lineLedgers,
        TradeDirection direction,
        GstDocumentKind kind,
        bool reverseCharge,
        GstPostingAccounts accounts)
    {
        if (lineLedgers.Count != invoice.Lines.Count)
            throw new ArgumentException("Each invoice line needs exactly one ledger.", nameof(lineLedgers));
        if (reverseCharge && direction == TradeDirection.Outward)
            throw new ArgumentException("Reverse charge applies only to inward documents.", nameof(reverseCharge));

        var lines = new List<PostingLine>();
        void Add(LedgerId ledger, Money amount) => lines.Add(new PostingLine(ledger, amount));

        if (direction == TradeDirection.Outward)
        {
            Add(accounts.Party, invoice.GrandTotal);
            for (var i = 0; i < lineLedgers.Count; i++) Add(lineLedgers[i], -invoice.Lines[i].Taxable);
            AddTaxes(lines, invoice, accounts.Output, credit: true);
            Add(accounts.RoundOff, -invoice.RoundOff);
        }
        else
        {
            for (var i = 0; i < lineLedgers.Count; i++) Add(lineLedgers[i], invoice.Lines[i].Taxable);
            AddTaxes(lines, invoice, accounts.Input, credit: false);
            // Under reverse charge the business owes the tax itself, payable in cash.
            if (reverseCharge) AddTaxes(lines, invoice, accounts.Output, credit: true);
            Add(accounts.Party, -invoice.GrandTotal);
            Add(accounts.RoundOff, invoice.RoundOff);
        }

        var sign = kind == GstDocumentKind.CreditNote ? -1 : 1;
        return Postings.Consolidate(sign == 1 ? lines : lines.Select(l => l with { Amount = -l.Amount }));
    }

    static void AddTaxes(List<PostingLine> lines, InvoiceResult invoice, TaxLedgers ledgers, bool credit)
    {
        void Add(LedgerId ledger, Money amount)
        {
            if (amount != Money.Zero) lines.Add(new PostingLine(ledger, credit ? -amount : amount));
        }

        Add(ledgers.Cgst, invoice.Cgst);
        Add(ledgers.Sgst, invoice.Sgst);
        Add(ledgers.Utgst, invoice.Utgst);
        Add(ledgers.Igst, invoice.Igst);
        Add(ledgers.Cess, invoice.Cess);
    }
}
