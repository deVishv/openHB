using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>GST documents: the host recalculates every line, tax, and total with Core before saving (spec 4, 5.3).</summary>
public sealed partial class VoucherService
{
    static readonly GstData Gst = GstData.LoadEmbedded();

    public Task<SavedVoucher> SaveInvoiceAsync(CallContext ctx, int company, InvoiceInput input)
    {
        CheckDocumentType(input);
        return SaveAsync(ctx, company, input.Type, input.Date, input.Narration, (tx, chart) => Calculated(tx, chart, input),
            input.Allocations ?? [], input.RequestId);
    }

    public Task<SavedVoucher> EditInvoiceAsync(CallContext ctx, int company, long id, long rowVersion, InvoiceInput input)
    {
        CheckDocumentType(input);
        return EditAsync(ctx, company, id, rowVersion, input.Type, input.Date, input.Narration, (tx, chart) => Calculated(tx, chart, input),
            input.Allocations);
    }

    static void CheckDocumentType(InvoiceInput input)
    {
        if (JournalTypes.Contains(input.Type)) throw new ServiceException(ServiceError.Invalid, $"Use the journal entry for {input.Type}.");
        var expected = input.Type switch
        {
            VoucherType.Sale => TradeDirection.Outward,
            VoucherType.Purchase => TradeDirection.Inward,
            _ => input.Direction,
        };
        if (input.Direction != expected) throw new ServiceException(ServiceError.Invalid, "Sale must be outward and purchase inward.");
        // Export, SEZ, and deemed export kinds describe this business's own supplies.
        if (input.Direction == TradeDirection.Inward && input.Kind != InvoiceKind.Regular)
            throw new ServiceException(ServiceError.Invalid, "Choose Regular for a document received from a supplier.");
        if (input.ReverseCharge && input.Direction == TradeDirection.Outward)
            throw new ServiceException(ServiceError.Invalid, "Reverse charge applies only to purchases.");
    }

    (IReadOnlyList<PostingLine>, InvoiceHeader?) Calculated(SqliteTransaction tx, ChartOfAccounts chart, InvoiceInput input)
    {
        var c = tx.Connection!;
        var party = PartyRepository.Get(c, input.Party) ?? throw new ServiceException(ServiceError.Invalid, "Unknown party.");
        var profile = CompanyRepository.GetProfile(c);

        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (line.ItemId is { } item && ItemRepository.Get(c, item) is null)
                throw new ServiceException(ServiceError.Invalid, $"Line {i + 1}: unknown item.");
            if (!IsLineLedger(c, chart, line.Ledger, input.Direction))
                throw new ServiceException(ServiceError.Invalid, $"Line {i + 1}: choose a sales or purchase ledger.");
            if (!Gst.IsValidRate(line.Rate, input.Date))
                throw new ServiceException(ServiceError.Invalid, $"GST rate {Text.Rate(line.Rate)}% is not valid on {Text.Date(input.Date)}.");
        }

        var taxFree = TaxFree(profile.Registration, input);
        // The supplier is this business for outward documents, and the party for inward ones.
        var supplier = input.Direction == TradeDirection.Outward ? profile.State : party.State;
        var sezOrExport = input.Kind is not (InvoiceKind.Regular or InvoiceKind.DeemedExport);
        var split = GstRules.Split(supplier, input.PlaceOfSupply, sezOrExport, Gst);
        var roundOff = CompanyRepository.GetSetting(c, "invoice.roundOff") is not { } json || JsonSerializer.Deserialize<bool>(json);

        InvoiceResult result;
        try
        {
            result = InvoiceCalculator.Calculate(
                [.. input.Lines.Select(l => new InvoiceLineInput(l.Quantity, l.UnitPrice, l.Discount,
                    taxFree ? new GstRate(0) : l.Rate, taxFree ? new GstRate(0) : l.CessRate, l.PriceIncludesTax))],
                split, roundOff, input.ReverseCharge);
        }
        catch (InvoiceException e)
        {
            throw new ServiceException(ServiceError.Invalid, InvoiceMessage(e));
        }

        var kind = input.Type switch
        {
            VoucherType.CreditNote => GstDocumentKind.CreditNote,
            VoucherType.DebitNote => GstDocumentKind.DebitNote,
            _ => GstDocumentKind.Invoice,
        };
        var postings = GstPosting.Post(result, [.. input.Lines.Select(l => l.Ledger)], input.Direction, kind, input.ReverseCharge,
            GstPostingAccounts.FromChart(chart, input.Party), inputCredit: profile.Registration == GstRegistration.Regular);
        Check(new VoucherDraft(input.Type, input.Date, postings), chart);

        var rows = input.Lines.Select((l, i) => new InvoiceLineRow(
            l.ItemId, l.Ledger, l.Description.Trim(), l.HsnSac.Trim(), l.Quantity, l.UnitPrice, l.Discount, l.Rate, l.CessRate,
            l.PriceIncludesTax, result.Lines[i].Taxable, result.Lines[i].Cgst, result.Lines[i].Sgst, result.Lines[i].Utgst,
            result.Lines[i].Igst, result.Lines[i].Cess)).ToList();
        var header = new InvoiceHeader(input.Party, input.Direction, input.Kind, input.PlaceOfSupply, input.ReverseCharge,
            input.PartyDocNumber?.Trim(), input.PartyDocDate, input.OriginalNumber?.Trim(), input.OriginalDate, result.RoundOff, rows,
            CheckedExport(input.Kind, input.Export), CheckedShipTo(input.Direction, input.Kind, input.ShipTo),
            // As they are now, so a reprint shows what this save issued even after the masters change (owner, 2026-11-03).
            new DocumentParty(chart.Ledger(input.Party).Name, party.Gstin?.Value, party.Address, party.State),
            new DocumentParty(profile.Name, profile.Gstin?.Value, profile.Address, profile.State));
        return (postings, header);
    }

    // The rule lives in Core (TaxMode), so the client's live totals follow it too.
    static bool TaxFree(GstRegistration registration, InvoiceInput input) =>
        TaxMode.Decide(registration, input.Kind, input.Direction, input.ReverseCharge,
            input.Lines.Any(l => l.Rate.BasisPoints != 0 || l.CessRate.BasisPoints != 0)) switch
        {
            TaxDecision.Taxed => false,
            TaxDecision.TaxFree => true,
            TaxDecision.RefuseUnregisteredReverseCharge =>
                throw new ServiceException(ServiceError.Invalid, "An unregistered company cannot pay reverse charge. Register for GST first."),
            TaxDecision.RefuseInputTax => throw new ServiceException(ServiceError.Invalid,
                "This company cannot take input tax credit. Enter the purchase with rate 0 and the full amount."),
            var d => throw new ArgumentOutOfRangeException(nameof(registration), d, null),
        };

    // Outward lines go to income ledgers. Inward lines go to expense ledgers, or asset ledgers other than cash and bank.
    // Tax, round off, and other required ledgers, and party ledgers, are never invoice lines.
    static bool IsLineLedger(SqliteConnection c, ChartOfAccounts chart, LedgerId id, TradeDirection direction)
    {
        if (!chart.Contains(id)) return false;
        if (chart.Ledger(id).Required is { } r && r is not (RequiredLedger.Sales or RequiredLedger.Purchases)) return false;
        if (PartyRepository.Get(c, id) is not null) return false;
        return (direction, chart.NatureOf(id)) switch
        {
            (TradeDirection.Outward, AccountNature.Income) => true,
            (TradeDirection.Inward, AccountNature.Expense) => true,
            (TradeDirection.Inward, AccountNature.Asset) => !chart.IsCashOrBank(id),
            _ => false,
        };
    }

    static string InvoiceMessage(InvoiceException e)
    {
        var text = e.Error switch
        {
            InvoiceError.NoLines => "Enter at least one line.",
            InvoiceError.NonPositiveQuantity => "quantity must be more than zero.",
            InvoiceError.NegativePrice => "price cannot be negative.",
            InvoiceError.NegativeDiscount => "discount cannot be negative.",
            InvoiceError.NegativeTaxable => "discount is more than the amount.",
            _ => throw new ArgumentOutOfRangeException(nameof(e)),
        };
        return e.LineIndex is { } i ? $"Line {i + 1}: {text}" : text;
    }
}
