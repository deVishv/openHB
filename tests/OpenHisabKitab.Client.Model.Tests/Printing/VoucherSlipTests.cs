using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

/// <summary>Plain voucher slips for purchases, received notes, receipts, payments, journals, and contras.</summary>
public sealed class VoucherSlipTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Required(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task<int> Party(ShellViewModel shell, string name, string kind = "TradePayables")
    {
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == kind).Id;
        var id = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = shell.Context.Company!.Id, Name = name, Group = group, Party = new PartyMsg { State = "27", Address = "Pune" },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(id) && shell.Context.Lookups.LedgerName(id) == name);
        return id;
    }

    async Task Journal(ShellViewModel shell, string type, int debit, int credit, long paise, string narration = "")
    {
        await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = type, Date = "2026-05-10", Narration = narration,
                Lines = { new PostingLineMsg { Ledger = debit, Paise = paise }, new PostingLineMsg { Ledger = credit, Paise = -paise } },
            },
        }, t.Headers());
    }

    static async Task<PrintDocument> Print(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        var screen = Assert.IsAssignableFrom<IPrintable>(shell.Tabs.Active!.Screen);
        var result = await screen.PrintAsync(PrintTarget.Paper);
        Assert.Null(result.Refusal);
        return result.Document!;
    }

    static List<string> Texts(PrintDocument d) => [.. d.Pages.SelectMany(p => p.Items).OfType<PrintText>().Select(x => x.Text)];

    [Fact]
    public async Task Payment_slip_lists_dr_cr_lines_total_and_signature_boxes()
    {
        var shell = await Shell();
        var supplier = await Party(shell, "Pune Steel");
        await Journal(shell, "Payment", supplier, Required(shell, "Cash"), 12345_60, "May rent");
        var doc = await Print(shell, "PYMT PAY/26-27/00001");
        var texts = Texts(doc);
        Assert.Single(doc.Pages);
        Assert.Contains("Payment voucher", texts);
        Assert.Contains("A Traders", texts);
        Assert.Contains("Voucher no. PAY/26-27/00001", texts);
        Assert.Contains("Date 10-05-2026", texts);
        Assert.Equal(["Ledger", "Debit", "Credit"], texts.Where(x => x is "Ledger" or "Debit" or "Credit"));
        Assert.Contains("Pune Steel", texts);
        Assert.Contains("Cash", texts);
        Assert.Equal(4, texts.Count(x => x == "12,345.60"));
        Assert.Contains("Total", texts);
        Assert.Contains("Narration: May rent", texts);
        Assert.Contains("Prepared by", texts);
        Assert.Contains("Checked by", texts);
        Assert.Contains("Approved by", texts);
        Assert.Contains("Receiver's signature", texts);
        Assert.Contains("Page 1 of 1", texts);
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Receipt_slip_has_receivers_signature()
    {
        var shell = await Shell();
        await Journal(shell, "Receipt", Required(shell, "Cash"), Required(shell, "Sales"), 500_00);
        Assert.Contains("Receiver's signature", Texts(await Print(shell, "RCPT RCT/26-27/00001")));
        await Journal(shell, "Journal", Required(shell, "Cash"), Required(shell, "Sales"), 500_00);
        var journal = Texts(await Print(shell, "JRNL JV/26-27/00001"));
        Assert.Contains("Journal voucher", journal);
        Assert.DoesNotContain("Receiver's signature", journal);
    }

    [Fact]
    public async Task Purchase_slip_shows_supplier_document()
    {
        var shell = await Shell();
        var supplier = await Party(shell, "Pune Steel");
        await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = shell.Context.Company!.Id,
            Invoice = new InvoiceMsg
            {
                Type = "Purchase", Date = "2026-06-01", Party = supplier, Direction = "Inward", Kind = "Regular", PlaceOfSupply = "27",
                PartyDocNumber = "B-77", PartyDocDate = "2026-05-30",
                Lines = { new InvoiceLineMsg { Ledger = Required(shell, "Purchases"), Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = 1000_00, RateBp = 1800 } },
            },
        }, t.Headers());
        var texts = Texts(await Print(shell, "PURC PUR/26-27/00001"));
        Assert.Contains("Purchase voucher", texts);
        Assert.Contains("Supplier document B-77, 30-05-2026", texts);
        Assert.Contains("1,180.00", texts);
        Assert.DoesNotContain("Tax invoice", texts);
        Assert.DoesNotContain("Receiver's signature", texts);
    }

    [Fact]
    public async Task Received_credit_note_prints_as_a_slip_not_an_invoice()
    {
        var shell = await Shell();
        var supplier = await Party(shell, "Pune Steel");
        await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = shell.Context.Company!.Id,
            Invoice = new InvoiceMsg
            {
                Type = "CreditNote", Date = "2026-06-01", Party = supplier, Direction = "Inward", Kind = "Regular", PlaceOfSupply = "27",
                PartyDocNumber = "SCN-4", PartyDocDate = "2026-05-31", OriginalNumber = "B-77", OriginalDate = "2026-05-30",
                Lines = { new InvoiceLineMsg { Ledger = Required(shell, "Purchases"), Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = 100_00, RateBp = 1800 } },
            },
        }, t.Headers());
        var texts = Texts(await Print(shell, "CRNT CN/26-27/00001"));
        Assert.Contains("Credit note received", texts);
        Assert.Contains("Supplier document SCN-4, 31-05-2026", texts);
        Assert.DoesNotContain("Credit note", texts);
        Assert.DoesNotContain("ORIGINAL FOR RECIPIENT", texts);
    }

    [Fact]
    public async Task Cancelled_slip_says_cancelled_without_a_zero_total()
    {
        var shell = await Shell();
        var saved = await t.OtherOwnerClient().Vouchers.SaveJournalAsync(new JournalRequest
        {
            Company = shell.Context.Company!.Id,
            Journal = new JournalMsg
            {
                Type = "Receipt", Date = "2026-05-10", Narration = "counter sale",
                Lines =
                {
                    new PostingLineMsg { Ledger = Required(shell, "Cash"), Paise = 500_00 },
                    new PostingLineMsg { Ledger = Required(shell, "Sales"), Paise = -500_00 },
                },
            },
        }, t.Headers());
        await t.OtherOwnerClient().Vouchers.CancelAsync(new CancelRequest
        {
            Company = shell.Context.Company!.Id, Id = saved.Id, RowVersion = saved.RowVersion, Reason = "typed twice",
        }, t.Headers());
        var texts = Texts(await Print(shell, "RCPT RCT/26-27/00001"));
        Assert.Contains("CANCELLED", texts);
        Assert.Contains("Narration: counter sale", texts);
        Assert.DoesNotContain("Total", texts);
        Assert.DoesNotContain(texts, x => x.StartsWith("Amount in words", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Slip_amount_in_words_is_the_debit_total()
    {
        var shell = await Shell();
        var bank = (int)(await t.OtherOwnerClient().Masters.AddLedgerAsync(new AddLedgerRequest
        {
            Company = shell.Context.Company!.Id, Name = "HDFC Bank", Group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "BankAccounts").Id,
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.LedgerName(bank) == "HDFC Bank");
        await Journal(shell, "Contra", bank, Required(shell, "Cash"), 1_00_000_50);
        var texts = Texts(await Print(shell, "CNTR CON/26-27/00001"));
        Assert.Contains("Contra voucher", texts);
        Assert.Contains("Amount in words: Rupees One Lakh and Fifty Paise Only", texts);
    }
}
