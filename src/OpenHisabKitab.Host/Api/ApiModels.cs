using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Host.Application;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Api;

// Response records of the REST API and the MCP tools. Money, Quantity, and GstRate stay typed so ApiJson renders them
// as wire strings for REST and with Indian text for MCP. Every ledger id is paired with the ledger's name.

public sealed record CompanyDto(int Id, string Name);

public sealed record CompanyDetailDto(int Id, string Name, string State, GstRegistration Registration, string? Gstin, string Address, int FirstYear);

public sealed record YearDto(int StartYear, DateOnly From, DateOnly To);

public sealed record GroupDto(int Id, string Name, int? ParentId, AccountNature Nature);

public sealed record LedgerDto(int Id, string Name, int GroupId, RequiredLedger? Required, Money Opening);

public sealed record PartyDto(int LedgerId, string Name, string? Gstin, string State, int CreditDays, string Address, long RowVersion);

public sealed record ItemDto(
    int Id, string Name, string HsnSac, bool IsService, string Unit, GstRate Rate, GstRate CessRate, Money DefaultPrice, long RowVersion);

public sealed record PostingDto(int LedgerId, string Ledger, Money Amount);

public sealed record InvoiceLineDto(
    int? ItemId, int LedgerId, string Ledger, string Description, string HsnSac, Quantity Quantity, Money UnitPrice, Money Discount,
    GstRate Rate, GstRate CessRate, bool PriceIncludesTax, Money Taxable, Money Cgst, Money Sgst, Money Utgst, Money Igst, Money Cess);

public sealed record InvoiceDto(
    int PartyId, string Party, TradeDirection Direction, InvoiceKind Kind, string PlaceOfSupply, bool ReverseCharge,
    string? PartyDocNumber, DateOnly? PartyDocDate, string? OriginalNumber, DateOnly? OriginalDate, Money RoundOff,
    IReadOnlyList<InvoiceLineDto> Lines, string? PortCode, string? ShippingBillNumber, DateOnly? ShippingBillDate,
    ShipToDto? ShipTo, DocumentPartyDto? PartyDetails, DocumentPartyDto? CompanyDetails);

/// <summary>Where goods go, for documents this business issues. State is null for an export with only a country.</summary>
public sealed record ShipToDto(string Name, string Address, string? State, string? Country);

/// <summary>The buyer's or the company's details as saved on the invoice; null for invoices saved before Plan 6b.</summary>
public sealed record DocumentPartyDto(string Name, string? Gstin, string Address, string State);

/// <summary>Part of a receipt, payment, or note allocated to one bill of a party.</summary>
public sealed record AllocationDto(int PartyId, string Party, BillSource BillSource, long BillId, string BillNumber, Money Amount);

/// <summary>A bill of a party: an invoice or note, or an opening bill. Open is what is still owed on it.</summary>
public sealed record BillDto(
    BillSource Source, long Id, string Number, string? PartyDocNumber, DateOnly Date, DateOnly? DueDate, Money Amount, Money Settled, Money Open);

public sealed record OpeningBillDto(long Id, string Number, DateOnly Date, DateOnly? DueDate, Money Amount);

/// <summary>A party's opening bills. Send Version back with a change; it moves on every save.</summary>
public sealed record OpeningBillsDto(long Version, IReadOnlyList<OpeningBillDto> Bills);

public sealed record VersionDto(long Version);

public sealed record VoucherDto(
    long Id, VoucherType Type, string Number, DateOnly Date, string Narration, bool Cancelled, long RowVersion,
    IReadOnlyList<PostingDto> Postings, InvoiceDto? Invoice, IReadOnlyList<AllocationDto> Allocations);

public sealed record TrialBalanceRowDto(int LedgerId, string Ledger, Money Opening, Money Debits, Money Credits, Money Closing);

public sealed record TrialBalanceDto(
    DateOnly From, DateOnly To, IReadOnlyList<TrialBalanceRowDto> Rows, Money OpeningDifference, Money TotalDebit, Money TotalCredit);

public sealed record ReportLineDto(int LedgerId, string Ledger, Money Amount);

public sealed record ProfitAndLossDto(
    DateOnly From, DateOnly To, IReadOnlyList<ReportLineDto> Income, IReadOnlyList<ReportLineDto> Expenses,
    Money TotalIncome, Money TotalExpenses, Money NetProfit);

public sealed record BalanceSheetDto(
    DateOnly AsAt, IReadOnlyList<ReportLineDto> Assets, IReadOnlyList<ReportLineDto> Liabilities, Money CurrentProfit,
    Money OpeningDifference, Money TotalAssets, Money TotalLiabilities);

public sealed record StatementRowDto(DateOnly Date, long VoucherId, string VoucherNumber, Money Amount, Money Balance);

public sealed record StatementDto(
    int LedgerId, string Ledger, DateOnly From, DateOnly To, Money Opening, IReadOnlyList<StatementRowDto> Rows, Money Closing);

public sealed record LayoutLineDto(LayoutSide Side, LayoutLineKind Kind, int Depth, string Label, int? GroupId, int? LedgerId,
    Money Current, Money? Previous);

public sealed record LayoutDto(LayoutStyle Style, bool HasPrevious, IReadOnlyList<LayoutLineDto> Lines);

public sealed record CashFlowLineDto(CashFlowSection Section, CashFlowItem Item, string Label, int? GroupId, string? Group, Money Amount);

public sealed record CashFlowDto(
    DateOnly From, DateOnly To, IReadOnlyList<CashFlowLineDto> Lines, Money Operating, Money Investing, Money Financing,
    Money NetChange, Money OpeningCash, Money ClosingCash);

public sealed record OutstandingBillDto(
    BillSource Source, long Id, string Number, string? PartyDocNumber, DateOnly Date, DateOnly Due, Money Amount, Money Open,
    int DaysOverdue, int Bucket, VoucherType? VoucherType);

public sealed record OutstandingPartyDto(
    int LedgerId, string Party, IReadOnlyList<OutstandingBillDto> Bills, IReadOnlyList<Money> Buckets, Money OnAccount, Money Total);

public sealed record OutstandingDto(
    DateOnly AsAt, PartySide Side, IReadOnlyList<int> AgeingDays, IReadOnlyList<OutstandingPartyDto> Parties,
    IReadOnlyList<Money> BucketTotals, Money OnAccountTotal, Money Total);

/// <summary>Service records to response records. Presentation only: no rule and no lookup beyond ledger names.</summary>
public static class ApiMap
{
    static string Name(ChartOfAccounts chart, LedgerId id) => chart.Contains(id) ? chart.Ledger(id).Name : "";

    public static CompanyDto ToDto(CompanyListEntry e) => new(e.Id, e.Name);

    public static CompanyDetailDto ToDto(int id, CompanyProfile p) =>
        new(id, p.Name, p.State.Value, p.Registration, p.Gstin?.Value, p.Address, p.FirstFyStartYear);

    public static YearDto ToDto(FinancialYear y) => new(y.StartYear, y.Start, y.End);

    public static GroupDto ToDto(AccountGroup g) => new(g.Id.Value, g.Name, g.Parent?.Value, g.Nature);

    public static LedgerDto ToDto(LedgerAccount l, IReadOnlyDictionary<LedgerId, Money> openings) =>
        new(l.Id.Value, l.Name, l.Group.Value, l.Required, openings.TryGetValue(l.Id, out var o) ? o : Money.Zero);

    public static PartyDto ToDto(Party p, ChartOfAccounts chart) =>
        new(p.Ledger.Value, Name(chart, p.Ledger), p.Gstin?.Value, p.State.Value, p.CreditDays, p.Address, p.RowVersion);

    public static ItemDto ToDto(Item i) =>
        new(i.Id, i.Name, i.HsnSac, i.IsService, i.Unit, i.Rate, i.CessRate, i.DefaultPrice, i.RowVersion);

    /// <summary>billNumbers names the bills the voucher's allocations settle; a bill missing from it shows an empty number.</summary>
    public static VoucherDto ToDto(VoucherRow v, ChartOfAccounts chart, IReadOnlyDictionary<BillRef, string>? billNumbers = null) =>
        new(v.Id, v.Type, v.DisplayNumber, v.Date, v.Narration, v.Cancelled, v.RowVersion,
            [.. v.Postings.Select(p => new PostingDto(p.Ledger.Value, Name(chart, p.Ledger), p.Amount))],
            v.Invoice is { } h ? ToDto(h, chart) : null,
            [.. v.Allocations.Select(a => new AllocationDto(a.Party.Value, Name(chart, a.Party), a.Bill.Source, a.Bill.Id,
                billNumbers?.GetValueOrDefault(a.Bill) ?? "", a.Amount))]);

    public static BillDto ToDto(OpenBill b) =>
        new(b.Ref.Source, b.Ref.Id, b.Number, b.PartyDocNumber, b.Date, b.DueDate, b.Amount, b.Settled, b.Open);

    public static OpeningBillsDto ToDto(OpeningBills b) =>
        new(b.Version, [.. b.Bills.Select(x => new OpeningBillDto(x.Id, x.Number, x.Date, x.DueDate, x.Amount))]);

    static InvoiceDto ToDto(InvoiceHeader h, ChartOfAccounts chart) =>
        new(h.Party.Value, Name(chart, h.Party), h.Direction, h.Kind, h.PlaceOfSupply.Value, h.ReverseCharge, h.PartyDocNumber,
            h.PartyDocDate, h.OriginalNumber, h.OriginalDate, h.RoundOff,
            [.. h.Lines.Select(l => new InvoiceLineDto(l.ItemId, l.Ledger.Value, Name(chart, l.Ledger), l.Description, l.HsnSac,
                l.Quantity, l.UnitPrice, l.Discount, l.Rate, l.CessRate, l.PriceIncludesTax, l.Taxable, l.Cgst, l.Sgst, l.Utgst,
                l.Igst, l.Cess))],
            h.Export?.PortCode, h.Export?.ShippingBillNumber, h.Export?.ShippingBillDate,
            h.ShipTo is { } t ? new ShipToDto(t.Name, t.Address, t.State?.Value, t.Country) : null, ToDto(h.PartyDetails), ToDto(h.CompanyDetails));

    static DocumentPartyDto? ToDto(DocumentParty? p) => p is null ? null : new(p.Name, p.Gstin, p.Address, p.State.Value);

    public static TrialBalanceDto ToDto(TrialBalance tb, ChartOfAccounts chart) =>
        new(tb.Period.From, tb.Period.To,
            [.. tb.Rows.Select(r => new TrialBalanceRowDto(r.Ledger.Value, Name(chart, r.Ledger), r.Opening, r.Debits, r.Credits, r.Closing))],
            tb.OpeningDifference, tb.TotalDebit, tb.TotalCredit);

    static IReadOnlyList<ReportLineDto> Lines(IReadOnlyList<ReportRow> rows, ChartOfAccounts chart) =>
        [.. rows.Select(r => new ReportLineDto(r.Ledger.Value, Name(chart, r.Ledger), r.Amount))];

    public static ProfitAndLossDto ToDto(ProfitAndLoss pl, ChartOfAccounts chart) =>
        new(pl.Period.From, pl.Period.To, Lines(pl.Income, chart), Lines(pl.Expenses, chart), pl.TotalIncome, pl.TotalExpenses, pl.NetProfit);

    public static LayoutDto ToDto(StatementLayout s) =>
        new(s.Style, s.HasPrevious,
            [.. s.Lines.Select(l => new LayoutLineDto(l.Side, l.Kind, l.Depth, Text.Label(l), l.Group?.Value, l.Ledger?.Value, l.Current, l.Previous))]);

    public static CashFlowDto ToDto(CashFlowStatement c, ChartOfAccounts chart)
    {
        string? GroupName(GroupId? id) => id is { } g ? chart.Groups.FirstOrDefault(x => x.Id == g)?.Name : null;
        return new(c.Period.From, c.Period.To,
            [.. c.Lines.Select(l => new CashFlowLineDto(l.Section, l.Item, Text.Label(l, GroupName(l.Group) ?? ""), l.Group?.Value,
                GroupName(l.Group), l.Amount))],
            c.Operating, c.Investing, c.Financing, c.NetChange, c.OpeningCash, c.ClosingCash);
    }

    public static OutstandingDto ToDto(OutstandingReport r, ChartOfAccounts chart) =>
        new(r.AsAt, r.Side, r.Ageing.Days,
            [.. r.Parties.Select(p => new OutstandingPartyDto(p.Party.Value, Name(chart, p.Party),
                [.. p.Bills.Select(b => new OutstandingBillDto(b.Bill.Ref.Source, b.Bill.Ref.Id, b.Bill.Number, b.Bill.PartyDocNumber,
                    b.Bill.Date, b.Due, b.Bill.Amount, b.Bill.Open, b.DaysOverdue, b.Bucket, b.Bill.Type))],
                p.Buckets, p.OnAccount, p.Total))],
            r.BucketTotals, r.OnAccountTotal, r.Total);

    public static BalanceSheetDto ToDto(BalanceSheet bs, ChartOfAccounts chart) =>
        new(bs.AsAt, Lines(bs.Assets, chart), Lines(bs.Liabilities, chart), bs.CurrentProfit, bs.OpeningDifference, bs.TotalAssets,
            bs.TotalLiabilities);

    public static StatementDto ToDto(LedgerStatement s, ChartOfAccounts chart, IReadOnlyDictionary<long, string> numbers) =>
        new(s.Ledger.Value, Name(chart, s.Ledger), s.Period.From, s.Period.To, s.Opening,
            [.. s.Rows.Select(r => new StatementRowDto(r.Date, r.VoucherId, numbers.TryGetValue(r.VoucherId, out var n) ? n : "", r.Amount, r.Balance))],
            s.Closing);
}

// Request records. Amounts, quantities, rates, dates, and choices are strings, read by ApiParse, so REST and the MCP write
// tools share one schema that says "string" and one set of refusal messages.

public sealed record IdDto(long Id);

/// <summary>AlreadySaved is true when a retried save found the voucher its requestId had already made.</summary>
public sealed record SavedDto(long Id, string Number, long RowVersion, bool AlreadySaved = false);

public sealed record GroupRequest(string Name, int ParentId);

public sealed record LedgerRequest(string Name, int GroupId);

public sealed record RenameRequest(string Name);

public sealed record PartyRequest(string Name, int GroupId, string? Gstin, string State, int CreditDays, string Address, long? RowVersion = null);

public sealed record ItemRequest(
    string Name, string HsnSac, bool IsService, string Unit, string Rate, string CessRate, string DefaultPrice, long? RowVersion = null);

/// <summary>A ledger line; amount is positive for debit.</summary>
public sealed record LineRequest(int LedgerId, string Amount);

/// <summary>Part of a receipt, payment, or note to settle one bill. BillSource and BillId come from open-bills.</summary>
public sealed record AllocationRequest(int PartyId, string BillSource, long BillId, string Amount);

/// <summary>
/// RequestId, when sent, makes a retried save return the first voucher instead of saving a second. Allocations left out of
/// an edit keep the voucher's stored ones; an empty list frees them.
/// </summary>
public sealed record JournalRequest(
    string Type, string Date, string Narration, IReadOnlyList<LineRequest> Lines, IReadOnlyList<AllocationRequest>? Allocations = null,
    string? RequestId = null);

public sealed record InvoiceLineRequest(
    int? ItemId, int LedgerId, string Description, string HsnSac, string Quantity, string UnitPrice, string Discount, string Rate,
    string CessRate, bool PriceIncludesTax);

public sealed record InvoiceRequest(
    string Type, string Date, string Narration, int PartyId, string Direction, string Kind, string PlaceOfSupply, bool ReverseCharge,
    IReadOnlyList<InvoiceLineRequest> Lines, string? PartyDocNumber = null, string? PartyDocDate = null, string? OriginalNumber = null,
    string? OriginalDate = null, string? PortCode = null, string? ShippingBillNumber = null, string? ShippingBillDate = null,
    IReadOnlyList<AllocationRequest>? Allocations = null, string? RequestId = null, ShipToRequest? ShipTo = null);

/// <summary>Where goods go. State is a two digit code; an export may give a country instead.</summary>
public sealed record ShipToRequest(string Name, string Address, string? State = null, string? Country = null);

/// <summary>An opening bill to keep (with its Id) or add (without one).</summary>
public sealed record OpeningBillRequest(string Number, string Date, string Amount, long? Id = null, string? DueDate = null);

public sealed record OpeningBillsRequest(long Version, IReadOnlyList<OpeningBillRequest> Bills);

public sealed record CompanyUpdateRequest(string Name, string State, string Registration, string Address, string? Gstin = null);

public sealed record EditJournalRequest(long RowVersion, JournalRequest Voucher);

public sealed record EditInvoiceRequest(long RowVersion, InvoiceRequest Voucher);

public sealed record CancelRequest(long RowVersion, string Reason);
