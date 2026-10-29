using System.Text.Json;
using OpenHisabKitab.Host.Tests.Grpc;

namespace OpenHisabKitab.Host.Tests.Api;

/// <summary>A host with the API on and one Maharashtra company, plus helpers to seed it through the services.</summary>
public sealed class ApiBooks : IDisposable
{
    public static readonly FinancialYear Fy = new(2026);
    public static readonly DateRange Fy2026 = Fy.Range;

    public ApiBooks(bool mcp = false, bool mcpWrites = false, bool callerIsLoopback = true)
    {
        F = new HostFactory(callerIsLoopback);
        F.EnableApi(mcp, mcpWrites);
        Owner = F.Owner().Context;
        Company = F.Get<CompanyService>().Create(Owner, TestHost.Profile());
        var chart = Masters.Chart(Owner, Company);
        Cash = chart.Required(RequiredLedger.Cash);
        Sales = chart.Required(RequiredLedger.Sales);
        Customer = Masters.SavePartyAsync(Owner, Company, "Ravi Stores", chart.Group(GroupKind.TradeReceivables),
            new Party(default, null, new StateCode("27"), 30, "Pune", 0)).GetAwaiter().GetResult();
    }

    public HostFactory F { get; }
    public CallContext Owner { get; }
    public int Company { get; }
    public LedgerId Cash { get; }
    public LedgerId Sales { get; }
    public LedgerId Customer { get; }

    public MasterService Masters => F.Get<MasterService>();
    public VoucherService Vouchers => F.Get<VoucherService>();
    public AdminService Admin => F.Get<AdminService>();

    public HttpClient Client(bool readWrite = false) => F.ApiClient(F.NewToken(readWrite, readWrite ? "rw-script" : "ro-script"));

    public SavedVoucher Receipt(string amount, DateOnly? date = null, string narration = "cash sale") =>
        Vouchers.SaveJournalAsync(Owner, Company, new JournalInput(VoucherType.Receipt, date ?? new DateOnly(2026, 5, 10), narration,
            [new PostingLine(Cash, Money.Parse(amount)), new PostingLine(Sales, -Money.Parse(amount))])).GetAwaiter().GetResult();

    public SavedVoucher Sale(string price) =>
        Vouchers.SaveInvoiceAsync(Owner, Company, new InvoiceInput(VoucherType.Sale, new DateOnly(2026, 6, 1), "", Customer,
            TradeDirection.Outward, InvoiceKind.Regular, new StateCode("27"), false, null, null, null, null,
            [new InvoiceEntryLine(null, Sales, "Steel rod", "7214", new Quantity(2500), Money.Parse(price), Money.Zero,
                new GstRate(1800), new GstRate(0), false)])).GetAwaiter().GetResult();

    public static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    public void Dispose() => F.Dispose();
}
