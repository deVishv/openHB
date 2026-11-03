using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// A receipt, payment, journal, or contra as typed: ledger lines with signed amounts, positive for debit. Allocations are
/// the bills a receipt or payment settles. RequestId is the client's id for this save, so a retry returns the first voucher.
/// </summary>
public sealed record JournalInput(
    VoucherType Type, DateOnly Date, string Narration, IReadOnlyList<PostingLine> Lines,
    IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null);

/// <summary>
/// What the host assigned on save: the voucher's id, its number, and its row version for the next edit. AlreadySaved is
/// true when a retried save found the voucher its request id had already made.
/// </summary>
public sealed record SavedVoucher(long Id, string DisplayNumber, long RowVersion, bool AlreadySaved = false);

/// <summary>An invoice line as typed. Taxable value and tax are always calculated by the host.</summary>
public sealed record InvoiceEntryLine(
    int? ItemId, LedgerId Ledger, string Description, string HsnSac, Quantity Quantity, Money UnitPrice, Money Discount,
    GstRate Rate, GstRate CessRate, bool PriceIncludesTax);

/// <summary>
/// A sales or purchase invoice, credit note, or debit note as typed. Direction is Outward for documents this business
/// issues and Inward for those it receives; a sale is always Outward and a purchase always Inward.
/// </summary>
public sealed record InvoiceInput(
    VoucherType Type, DateOnly Date, string Narration, LedgerId Party, TradeDirection Direction, InvoiceKind Kind,
    StateCode PlaceOfSupply, bool ReverseCharge, string? PartyDocNumber, DateOnly? PartyDocDate,
    string? OriginalNumber, DateOnly? OriginalDate, IReadOnlyList<InvoiceEntryLine> Lines,
    ExportDetails? Export = null, IReadOnlyList<AllocationRow>? Allocations = null, string? RequestId = null, ShipTo? ShipTo = null);
