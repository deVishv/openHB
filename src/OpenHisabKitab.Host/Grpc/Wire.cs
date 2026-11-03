using System.Globalization;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Reports;
using OpenHisabKitab.Core.Time;
using OpenHisabKitab.Core.Vouchers;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.Backup;
using OpenHisabKitab.Data.CompanyDb;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Grpc;

/// <summary>
/// Translation between gRPC messages and the application's types. Bad text in a message becomes an Invalid error with a
/// one line message; no rule lives here.
/// </summary>
internal static class Wire
{
    static readonly GstData Gst = GstData.LoadEmbedded();
    const string DateFormat = "yyyy-MM-dd";

    /// <summary>A timestamp as round trip UTC text.</summary>
    public static string At(DateTimeOffset at) => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static DateOnly Date(string text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw Invalid("Use dates as yyyy-mm-dd.");

    public static DateOnly? OptionalDate(string text) => text.Length == 0 ? null : Date(text);

    public static string Text(DateOnly d) => d.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string Text(DateOnly? d) => d is { } x ? Text(x) : "";

    public static DateRange Range(Period? p)
    {
        if (p is null) throw Invalid("Choose a period.");
        var (from, to) = (Date(p.From), Date(p.To));
        if (to < from) throw Invalid("The period ends before it starts.");
        return new DateRange(from, to);
    }

    public static Period ToMsg(DateRange r) => new() { From = Text(r.From), To = Text(r.To) };

    public static T Enum<T>(string text, string what) where T : struct, System.Enum =>
        System.Enum.TryParse<T>(text, ignoreCase: true, out var v) && System.Enum.IsDefined(v) ? v : throw Invalid($"Unknown {what}: {text}.");

    public static StateCode State(string text)
    {
        var code = text.Trim();
        return code.Length == 2 && code.All(char.IsAsciiDigit) && Gst.States.Any(s => s.Code.Value == code)
            ? new StateCode(code) : throw Invalid($"Unknown state code: {text}.");
    }

    public static Gstin? OptionalGstin(string text) =>
        text.Trim().Length == 0 ? null
        : Gstin.TryParse(text, Gst, out var g) ? g
        : throw Invalid("GSTIN is not valid.");

    public static string? OptionalText(string text) => text.Trim().Length == 0 ? null : text;

    static ServiceException Invalid(string message) => new(ServiceError.Invalid, message);

    // Companies

    public static CompanyProfile Profile(CompanyProfileMsg m) =>
        new(m.Name, State(m.State), Enum<GstRegistration>(m.Registration, "registration"), OptionalGstin(m.Gstin), m.Address, m.FirstFyStartYear);

    public static CompanyProfileMsg ToMsg(CompanyProfile p) => new()
    {
        Name = p.Name, State = p.State.Value, Registration = p.Registration.ToString(), Gstin = p.Gstin?.Value ?? "",
        Address = p.Address, FirstFyStartYear = p.FirstFyStartYear,
    };

    public static YearMsg ToMsg(FinancialYear fy) => new() { StartYear = fy.StartYear, Label = fy.Label };

    public static SettingValues ToMsg(IReadOnlyDictionary<string, string> values)
    {
        var m = new SettingValues();
        m.Values.AddRange(values.Select(p => new SettingValue { Key = p.Key, Json = p.Value }));
        return m;
    }

    // Masters

    public static ChartMsg ToMsg(ChartOfAccounts chart, IReadOnlyDictionary<LedgerId, Money> openings)
    {
        var m = new ChartMsg();
        m.Groups.AddRange(chart.Groups.Select(g => new GroupMsg
        {
            Id = g.Id.Value, Name = g.Name, Parent = g.Parent?.Value ?? 0, Nature = g.Nature.ToString(), Kind = g.Kind.ToString(),
        }));
        m.Ledgers.AddRange(chart.Ledgers.Select(l => new LedgerMsg
        {
            Id = l.Id.Value, Name = l.Name, Group = l.Group.Value, Required = l.Required?.ToString() ?? "",
            OpeningPaise = openings.GetValueOrDefault(l.Id).Paise,
        }));
        return m;
    }

    public static Party Party(PartyMsg m) =>
        new(new LedgerId(m.Ledger), OptionalGstin(m.Gstin), State(m.State), m.CreditDays, m.Address, m.RowVersion);

    public static PartyMsg ToMsg(Party p) => new()
    {
        Ledger = p.Ledger.Value, Gstin = p.Gstin?.Value ?? "", State = p.State.Value, CreditDays = p.CreditDays, Address = p.Address,
        RowVersion = p.RowVersion,
    };

    public static Item Item(ItemMsg m) =>
        new(m.Id, m.Name, m.HsnSac, m.IsService, m.Unit, Rate(m.RateBp), Rate(m.CessBp), new Money(m.DefaultPricePaise), m.RowVersion);

    public static ItemMsg ToMsg(Item i) => new()
    {
        Id = i.Id, Name = i.Name, HsnSac = i.HsnSac, IsService = i.IsService, Unit = i.Unit, RateBp = i.Rate.BasisPoints,
        CessBp = i.CessRate.BasisPoints, DefaultPricePaise = i.DefaultPrice.Paise, RowVersion = i.RowVersion,
    };

    static GstRate Rate(int bp) => bp >= 0 ? new GstRate(bp) : throw Invalid("A GST rate cannot be negative.");

    // Vouchers

    public static JournalInput Journal(JournalMsg? m)
    {
        if (m is null) throw Invalid("Enter the voucher.");
        return new JournalInput(Enum<VoucherType>(m.Type, "voucher type"), Date(m.Date), m.Narration,
            [.. m.Lines.Select(l => new PostingLine(new LedgerId(l.Ledger), new Money(l.Paise)))],
            [.. m.Allocations.Select(Allocation)], OptionalText(m.RequestId));
    }

    public static InvoiceInput Invoice(InvoiceMsg? m)
    {
        if (m is null) throw Invalid("Enter the invoice.");
        return new InvoiceInput(
            Enum<VoucherType>(m.Type, "voucher type"), Date(m.Date), m.Narration, new LedgerId(m.Party),
            Enum<TradeDirection>(m.Direction, "direction"), Enum<InvoiceKind>(m.Kind, "invoice kind"), State(m.PlaceOfSupply),
            m.ReverseCharge, OptionalText(m.PartyDocNumber), OptionalDate(m.PartyDocDate), OptionalText(m.OriginalNumber),
            OptionalDate(m.OriginalDate),
            [.. m.Lines.Select(l => new InvoiceEntryLine(
                l.ItemId == 0 ? null : l.ItemId, new LedgerId(l.Ledger), l.Description, l.HsnSac, new Quantity(l.QtyThousandths),
                new Money(l.UnitPricePaise), new Money(l.DiscountPaise), Rate(l.RateBp), Rate(l.CessBp), l.PriceIncludesTax))],
            new ExportDetails(OptionalText(m.PortCode), OptionalText(m.ShippingBillNumber), OptionalDate(m.ShippingBillDate)),
            [.. m.Allocations.Select(Allocation)], OptionalText(m.RequestId),
            m.ShipTo is { } t ? new ShipTo(t.Name, t.Address, OptionalText(t.State) is { } st ? State(st) : null, OptionalText(t.Country)) : null);
    }

    static ShipToMsg? ToMsg(ShipTo? t) => t is null ? null
        : new ShipToMsg { Name = t.Name, Address = t.Address, State = t.State?.Value ?? "", Country = t.Country ?? "" };

    static DocumentPartyMsg? ToMsg(DocumentParty? p) => p is null ? null
        : new DocumentPartyMsg { Name = p.Name, Gstin = p.Gstin ?? "", Address = p.Address, State = p.State.Value };

    public static AllocationRow Allocation(AllocationMsg m) =>
        new(new LedgerId(m.Party), new BillRef(Enum<BillSource>(m.BillSource, "bill source"), m.BillId), new Money(m.Paise));

    public static AllocationMsg ToMsg(AllocationRow a) =>
        new() { Party = a.Party.Value, BillSource = a.Bill.Source.ToString(), BillId = a.Bill.Id, Paise = a.Amount.Paise };

    public static BillMsg ToMsg(OpenBill b) => new()
    {
        Source = b.Ref.Source.ToString(), Id = b.Ref.Id, Party = b.Party.Value, Number = b.Number, Date = Text(b.Date),
        DueDate = Text(b.DueDate), AmountPaise = b.Amount.Paise, SettledPaise = b.Settled.Paise, PartyDocNumber = b.PartyDocNumber ?? "",
    };

    public static OpeningBillRow OpeningBill(OpeningBillMsg m) =>
        new(m.Id, default, m.Number, Date(m.Date), OptionalDate(m.DueDate), new Money(m.AmountPaise));

    public static OpeningBillList ToMsg(OpeningBills b)
    {
        var m = new OpeningBillList { Version = b.Version };
        m.Bills.AddRange(b.Bills.Select(x => new OpeningBillMsg
        {
            Id = x.Id, Number = x.Number, Date = Text(x.Date), DueDate = Text(x.DueDate), AmountPaise = x.Amount.Paise,
        }));
        return m;
    }

    public static Application.PageRequest Page(PageMsg? m) =>
        m is null ? new Application.PageRequest() : new Application.PageRequest(m.Limit, OptionalText(m.After));

    public static SavedReply ToMsg(SavedVoucher s) =>
        new() { Id = s.Id, DisplayNumber = s.DisplayNumber, RowVersion = s.RowVersion, AlreadySaved = s.AlreadySaved };

    public static VoucherMsg ToMsg(VoucherRow v)
    {
        var m = new VoucherMsg
        {
            Id = v.Id, Type = v.Type.ToString(), Series = v.Series, FyStartYear = v.FyStartYear, Number = v.Number,
            DisplayNumber = v.DisplayNumber, Date = Text(v.Date), Narration = v.Narration, Cancelled = v.Cancelled,
            CreatedBy = v.CreatedBy, RowVersion = v.RowVersion,
        };
        m.Postings.AddRange(v.Postings.Select(p => new PostingLineMsg { Ledger = p.Ledger.Value, Paise = p.Amount.Paise }));
        m.Allocations.AddRange(v.Allocations.Select(ToMsg));
        if (v.Invoice is { } inv)
        {
            m.Invoice = new InvoiceHeaderMsg
            {
                Party = inv.Party.Value, Direction = inv.Direction.ToString(), Kind = inv.Kind.ToString(),
                PlaceOfSupply = inv.PlaceOfSupply.Value, ReverseCharge = inv.ReverseCharge, PartyDocNumber = inv.PartyDocNumber ?? "",
                PartyDocDate = Text(inv.PartyDocDate), OriginalNumber = inv.OriginalNumber ?? "", OriginalDate = Text(inv.OriginalDate),
                RoundOffPaise = inv.RoundOff.Paise, PortCode = inv.Export?.PortCode ?? "",
                ShippingBillNumber = inv.Export?.ShippingBillNumber ?? "", ShippingBillDate = Text(inv.Export?.ShippingBillDate),
                ShipTo = ToMsg(inv.ShipTo), PartyDetails = ToMsg(inv.PartyDetails), CompanyDetails = ToMsg(inv.CompanyDetails),
            };
            m.Invoice.Lines.AddRange(inv.Lines.Select(l => new StoredInvoiceLineMsg
            {
                Entry = new InvoiceLineMsg
                {
                    ItemId = l.ItemId ?? 0, Ledger = l.Ledger.Value, Description = l.Description, HsnSac = l.HsnSac,
                    QtyThousandths = l.Quantity.Thousandths, UnitPricePaise = l.UnitPrice.Paise, DiscountPaise = l.Discount.Paise,
                    RateBp = l.Rate.BasisPoints, CessBp = l.CessRate.BasisPoints, PriceIncludesTax = l.PriceIncludesTax,
                },
                TaxablePaise = l.Taxable.Paise, CgstPaise = l.Cgst.Paise, SgstPaise = l.Sgst.Paise, UtgstPaise = l.Utgst.Paise,
                IgstPaise = l.Igst.Paise, CessPaise = l.Cess.Paise,
            }));
        }
        return m;
    }

    // Reports

    public static TrialBalanceMsg ToMsg(TrialBalance tb)
    {
        var m = new TrialBalanceMsg
        {
            Period = ToMsg(tb.Period), OpeningDifference = tb.OpeningDifference.Paise, TotalDebit = tb.TotalDebit.Paise,
            TotalCredit = tb.TotalCredit.Paise,
        };
        m.Rows.AddRange(tb.Rows.Select(r => new TrialBalanceRowMsg
        {
            Ledger = r.Ledger.Value, Opening = r.Opening.Paise, Debits = r.Debits.Paise, Credits = r.Credits.Paise, Closing = r.Closing.Paise,
        }));
        return m;
    }

    public static ProfitAndLossMsg ToMsg(ProfitAndLoss p)
    {
        var m = new ProfitAndLossMsg
        {
            Period = ToMsg(p.Period), TotalIncome = p.TotalIncome.Paise, TotalExpenses = p.TotalExpenses.Paise, NetProfit = p.NetProfit.Paise,
        };
        m.Income.AddRange(p.Income.Select(Row));
        m.Expenses.AddRange(p.Expenses.Select(Row));
        return m;
    }

    public static BalanceSheetMsg ToMsg(BalanceSheet b)
    {
        var m = new BalanceSheetMsg
        {
            AsAt = Text(b.AsAt), CurrentProfit = b.CurrentProfit.Paise, OpeningDifference = b.OpeningDifference.Paise,
            TotalAssets = b.TotalAssets.Paise, TotalLiabilities = b.TotalLiabilities.Paise,
        };
        m.Assets.AddRange(b.Assets.Select(Row));
        m.Liabilities.AddRange(b.Liabilities.Select(Row));
        return m;
    }

    public static StatementMsg ToMsg(LedgerStatement s)
    {
        var m = new StatementMsg { Ledger = s.Ledger.Value, Period = ToMsg(s.Period), Opening = s.Opening.Paise, Closing = s.Closing.Paise };
        m.Rows.AddRange(s.Rows.Select(r => new StatementRowMsg
        {
            Date = Text(r.Date), VoucherId = r.VoucherId, Amount = r.Amount.Paise, Balance = r.Balance.Paise,
        }));
        return m;
    }

    static ReportRowMsg Row(ReportRow r) => new() { Ledger = r.Ledger.Value, Amount = r.Amount.Paise };

    public static LayoutStyle Style(string text) => text.Trim() switch
    {
        "" => LayoutStyle.ScheduleIII,
        var t when t.Equals("ScheduleIII", StringComparison.OrdinalIgnoreCase) => LayoutStyle.ScheduleIII,
        var t when t.Equals("Chart", StringComparison.OrdinalIgnoreCase) => LayoutStyle.Chart,
        _ => throw Invalid("style must be ScheduleIII or Chart."),
    };

    public static LayoutMsg ToMsg(StatementLayout s)
    {
        var m = new LayoutMsg { Style = s.Style.ToString(), HasPrevious = s.HasPrevious };
        m.Lines.AddRange(s.Lines.Select(l => new LayoutLineMsg
        {
            Side = l.Side.ToString(), Kind = l.Kind.ToString(), Depth = l.Depth, Label = l.Label, Group = l.Group?.Value ?? 0,
            Ledger = l.Ledger?.Value ?? 0, Current = l.Current.Paise, Previous = l.Previous?.Paise ?? 0,
        }));
        return m;
    }

    public static CashFlowMsg ToMsg(CashFlowStatement c)
    {
        var m = new CashFlowMsg
        {
            Period = ToMsg(c.Period), Operating = c.Operating.Paise, Investing = c.Investing.Paise, Financing = c.Financing.Paise,
            NetChange = c.NetChange.Paise, OpeningCash = c.OpeningCash.Paise, ClosingCash = c.ClosingCash.Paise,
        };
        m.Lines.AddRange(c.Lines.Select(l => new CashFlowLineMsg
        {
            Section = l.Section.ToString(), Item = l.Item.ToString(), Group = l.Group?.Value ?? 0, Amount = l.Amount.Paise,
        }));
        return m;
    }

    public static OutstandingMsg ToMsg(OutstandingReport r)
    {
        var m = new OutstandingMsg
        {
            AsAt = Text(r.AsAt), Side = r.Side.ToString(), OnAccountTotal = r.OnAccountTotal.Paise, Total = r.Total.Paise,
        };
        m.AgeingDays.AddRange(r.Ageing.Days);
        m.BucketTotals.AddRange(r.BucketTotals.Select(x => x.Paise));
        m.Parties.AddRange(r.Parties.Select(p =>
        {
            var party = new OutstandingPartyMsg { Party = p.Party.Value, OnAccount = p.OnAccount.Paise, Total = p.Total.Paise };
            party.Buckets.AddRange(p.Buckets.Select(x => x.Paise));
            party.Bills.AddRange(p.Bills.Select(b => new OutstandingBillMsg
            {
                Source = b.Bill.Ref.Source.ToString(), Id = b.Bill.Ref.Id, Number = b.Bill.Number, PartyDocNumber = b.Bill.PartyDocNumber ?? "",
                Date = Text(b.Bill.Date), Due = Text(b.Due), Amount = b.Bill.Amount.Paise, Open = b.Bill.Open.Paise,
                DaysOverdue = b.DaysOverdue, Bucket = b.Bucket, VoucherType = b.Bill.Type?.ToString() ?? "",
            }));
            return party;
        }));
        return m;
    }

    public static PartySide Side(string text) => text.Trim() switch
    {
        "" => PartySide.Receivable,
        var t when t.Equals("Receivable", StringComparison.OrdinalIgnoreCase) => PartySide.Receivable,
        var t when t.Equals("Payable", StringComparison.OrdinalIgnoreCase) => PartySide.Payable,
        _ => throw Invalid("side must be receivable or payable."),
    };

    public static StatementPageMsg ToMsg(StatementPage p)
    {
        var m = new StatementPageMsg
        {
            Ledger = p.Ledger.Value, Period = ToMsg(p.Period), Opening = p.Opening.Paise, Closing = p.Closing.Paise, Next = p.Next ?? "",
        };
        m.Lines.AddRange(p.Lines.Select(l =>
        {
            var line = new StatementLineMsg
            {
                Date = Text(l.Row.Date), VoucherId = l.Row.VoucherId, Type = l.Type.ToString(), Number = l.Number, Narration = l.Narration,
                Amount = l.Row.Amount.Paise, Balance = l.Row.Balance.Paise,
            };
            line.CounterLedgers.AddRange(l.Counter.Select(c => c.Value));
            return line;
        }));
        return m;
    }

    // Admin

    public static AuditFilter Filter(ListAuditRequest r) => new(
        Timestamp(r.From), Timestamp(r.To), OptionalText(r.User), OptionalText(r.Entity), r.Limit is > 0 and <= 5000 ? r.Limit : 500);

    static DateTimeOffset? Timestamp(string text) =>
        text.Length == 0 ? null
        : DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t
        : throw Invalid("Use timestamps in round trip format.");

    public static AuditEntryMsg ToMsg(StoredAuditEntry s) => new()
    {
        Seq = s.Seq, At = s.Entry.At.ToString("O", CultureInfo.InvariantCulture), User = s.Entry.User, Machine = s.Entry.Machine,
        Source = s.Entry.Source.ToString(), TokenName = s.Entry.TokenName ?? "", Action = s.Entry.Action, Entity = s.Entry.Entity,
        EntityKey = s.Entry.EntityKey, Before = s.Entry.Before ?? "", After = s.Entry.After ?? "",
    };

    public static BackupMsg ToMsg(BackupFile b) =>
        new() { Path = b.Path, Source = b.SourceName, TakenAt = b.TakenAt.ToString("O", CultureInfo.InvariantCulture) };

    public static int? Target(int company) => company == 0 ? null : company;
}
