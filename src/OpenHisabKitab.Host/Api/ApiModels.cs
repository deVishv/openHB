using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.CompanyDb;
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
    IReadOnlyList<InvoiceLineDto> Lines);

public sealed record VoucherDto(
    long Id, VoucherType Type, string Number, DateOnly Date, string Narration, bool Cancelled, long RowVersion,
    IReadOnlyList<PostingDto> Postings, InvoiceDto? Invoice);

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

    public static VoucherDto ToDto(VoucherRow v, ChartOfAccounts chart) =>
        new(v.Id, v.Type, v.DisplayNumber, v.Date, v.Narration, v.Cancelled, v.RowVersion,
            [.. v.Postings.Select(p => new PostingDto(p.Ledger.Value, Name(chart, p.Ledger), p.Amount))],
            v.Invoice is { } h ? ToDto(h, chart) : null);

    static InvoiceDto ToDto(InvoiceHeader h, ChartOfAccounts chart) =>
        new(h.Party.Value, Name(chart, h.Party), h.Direction, h.Kind, h.PlaceOfSupply.Value, h.ReverseCharge, h.PartyDocNumber,
            h.PartyDocDate, h.OriginalNumber, h.OriginalDate, h.RoundOff,
            [.. h.Lines.Select(l => new InvoiceLineDto(l.ItemId, l.Ledger.Value, Name(chart, l.Ledger), l.Description, l.HsnSac,
                l.Quantity, l.UnitPrice, l.Discount, l.Rate, l.CessRate, l.PriceIncludesTax, l.Taxable, l.Cgst, l.Sgst, l.Utgst,
                l.Igst, l.Cess))]);

    public static TrialBalanceDto ToDto(TrialBalance tb, ChartOfAccounts chart) =>
        new(tb.Period.From, tb.Period.To,
            [.. tb.Rows.Select(r => new TrialBalanceRowDto(r.Ledger.Value, Name(chart, r.Ledger), r.Opening, r.Debits, r.Credits, r.Closing))],
            tb.OpeningDifference, tb.TotalDebit, tb.TotalCredit);

    static IReadOnlyList<ReportLineDto> Lines(IReadOnlyList<ReportRow> rows, ChartOfAccounts chart) =>
        [.. rows.Select(r => new ReportLineDto(r.Ledger.Value, Name(chart, r.Ledger), r.Amount))];

    public static ProfitAndLossDto ToDto(ProfitAndLoss pl, ChartOfAccounts chart) =>
        new(pl.Period.From, pl.Period.To, Lines(pl.Income, chart), Lines(pl.Expenses, chart), pl.TotalIncome, pl.TotalExpenses, pl.NetProfit);

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

public sealed record SavedDto(long Id, string Number, long RowVersion);

public sealed record GroupRequest(string Name, int ParentId);

public sealed record LedgerRequest(string Name, int GroupId);

public sealed record RenameRequest(string Name);

public sealed record PartyRequest(string Name, int GroupId, string? Gstin, string State, int CreditDays, string Address, long? RowVersion = null);

public sealed record ItemRequest(
    string Name, string HsnSac, bool IsService, string Unit, string Rate, string CessRate, string DefaultPrice, long? RowVersion = null);

/// <summary>A ledger line; amount is positive for debit.</summary>
public sealed record LineRequest(int LedgerId, string Amount);

public sealed record JournalRequest(string Type, string Date, string Narration, IReadOnlyList<LineRequest> Lines);

public sealed record InvoiceLineRequest(
    int? ItemId, int LedgerId, string Description, string HsnSac, string Quantity, string UnitPrice, string Discount, string Rate,
    string CessRate, bool PriceIncludesTax);

public sealed record InvoiceRequest(
    string Type, string Date, string Narration, int PartyId, string Direction, string Kind, string PlaceOfSupply, bool ReverseCharge,
    IReadOnlyList<InvoiceLineRequest> Lines, string? PartyDocNumber = null, string? PartyDocDate = null, string? OriginalNumber = null,
    string? OriginalDate = null);

public sealed record EditJournalRequest(long RowVersion, JournalRequest Voucher);

public sealed record EditInvoiceRequest(long RowVersion, InvoiceRequest Voucher);

public sealed record CancelRequest(long RowVersion, string Reason);
