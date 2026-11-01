using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Host.Api;
using OpenHisabKitab.Host.Application;

namespace OpenHisabKitab.Host.Mcp;

/// <summary>
/// MCP tools that change the books (spec 4.1). They stay listed when writes are off; the token's context then refuses them
/// with how to turn writes on. Everything they save is audited with source MCP and the token's name.
/// </summary>
[McpServerToolType]
public sealed class WriteTools(IHttpContextAccessor http, MasterService masters, VoucherService vouchers, ILogger<WriteTools> log)
{
    const string Off = " Off unless the Owner turns on mcp.allowWrites and the token can write.";
    const string Amounts = " Amounts, quantities, and rates are strings like \"12345.60\", \"2.500\", and \"18.00\". Dates are yyyy-mm-dd.";
    const string Bills = " To settle bills, add allocations: the party, the bill's source and id from open_bills, and the amount. " +
                         "What is not allocated stays on account.";

    CallContext Caller => (http.HttpContext ?? throw new ServiceException(ServiceError.NotAuthenticated, "Send a valid API token.")).Caller();

    [McpServerTool(Name = "create_voucher", Destructive = false),
     Description("Saves a receipt, payment, journal, or contra. Each line's amount is positive for debit and negative for credit, " +
                 "and the lines must balance. Returns the voucher number." + Bills + Amounts + Off)]
    public Task<CallToolResult> CreateVoucher(int company, JournalRequest voucher) => McpResults.Run(log, "create_voucher", async () =>
    {
        var s = await vouchers.SaveJournalAsync(Caller, company, ApiParse.Journal(voucher, "$.voucher"));
        return (object)new SavedDto(s.Id, s.DisplayNumber, s.RowVersion, s.AlreadySaved);
    });

    [McpServerTool(Name = "create_invoice", Destructive = false),
     Description("Saves a sales or purchase invoice, credit note, or debit note. The host calculates taxable value and GST. " +
                 "Returns the voucher number." + Bills + Amounts + Off)]
    public Task<CallToolResult> CreateInvoice(int company, InvoiceRequest invoice) => McpResults.Run(log, "create_invoice", async () =>
    {
        var s = await vouchers.SaveInvoiceAsync(Caller, company, ApiParse.Invoice(invoice, "$.invoice"));
        return (object)new SavedDto(s.Id, s.DisplayNumber, s.RowVersion, s.AlreadySaved);
    });

    [McpServerTool(Name = "create_party", Destructive = false),
     Description("Creates a customer or supplier under a Trade Receivables or Trade Payables group. State is the two digit GST state code." + Off)]
    public Task<CallToolResult> CreateParty(int company, PartyRequest party) => McpResults.Run(log, "create_party", async () =>
    {
        var id = await masters.SavePartyAsync(Caller, company, party.Name, new GroupId(party.GroupId), ApiParse.Party(party, default, 0));
        return (object)new IdDto(id.Value);
    });
}
