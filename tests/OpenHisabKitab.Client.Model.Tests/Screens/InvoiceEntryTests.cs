using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class InvoiceEntryTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<InvoiceEntryViewModel> Open(ShellContext ctx, string input)
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var s = (InvoiceEntryViewModel)(await InvoiceEntryViewModel.CreateAsync(cmd, ctx))!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    async Task<LedgerOption> Party(ShellViewModel shell, string name, string state, string kind = "TradeReceivables")
    {
        var company = shell.Context.Company!.Id;
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == kind).Id;
        var id = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = company, Name = name, Group = group, Party = new PartyMsg { State = state },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(id));
        return shell.Context.Lookups.Ledgers(LedgerUse.Party).Single(l => l.Id == id);
    }

    async Task<ItemMsg> Item(ShellViewModel shell)
    {
        await t.OtherOwnerClient().Masters.SaveItemAsync(new SaveItemRequest
        {
            Company = shell.Context.Company!.Id,
            Item = new ItemMsg { Name = "Steel rod", HsnSac = "7214", Unit = "kg", RateBp = 1800, DefaultPricePaise = 500_00 },
        }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.Items.Count == 1);
        return shell.Context.Lookups.Items.Single();
    }

    static void FillLine(InvoiceLine line, LedgerOption ledger, string price, string rate = "18")
    {
        line.Ledger.Set(ledger);
        line.Description = "Rod";
        line.HsnSac = "7214";
        line.Quantity = "1";
        line.Price = price;
        line.Rate = rate;
    }

    static LedgerOption Sales(ShellContext ctx) => ctx.Lookups.Ledgers(LedgerUse.Any).Single(l => l.Required == "Sales");

    [Fact]
    public async Task Picking_an_item_fills_the_line()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var item = await Item(shell);
        var s = await Open(shell.Context, "SALE");
        s.Lines[0].Item.Set(item);
        var line = s.Lines[0];
        Assert.Equal(("Steel rod", "7214", "18", "500.00", "Sales"), (line.Description, line.HsnSac, line.Rate, line.Price, line.Ledger.Value!.Required));
    }

    [Fact]
    public async Task Place_of_supply_defaults_from_the_party()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await Party(shell, "Blr Mart", "29");
        var s = await Open(shell.Context, "SALE");
        s.Party.Set(party);
        Assert.Equal("29", s.PlaceOfSupply!.Code);
        FillLine(s.Lines[0], Sales(shell.Context), "1000");
        Assert.Equal(18000, s.TotalsView.Igst.Paise);
        Assert.Equal(118000, s.TotalsView.Total.Paise);
    }

    [Fact]
    public async Task New_line_takes_prices_with_tax_when_the_setting_is_on()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        await t.OtherOwnerClient().Companies.SetSettingAsync(new SettingRequest { Company = shell.Context.Company!.Id, Key = "invoice.pricesIncludeTax", Json = "true" }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.CompanySettings.ContainsKey("invoice.pricesIncludeTax"));
        var s = await Open(shell.Context, "SALE");
        Assert.True(s.Lines[0].PriceIncludesTax);
    }

    [Fact]
    public async Task Export_fields_show_only_for_export_kinds()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "SALE");
        Assert.False(s.ShowsExport);
        s.Kind = "ExportUnderLut";
        Assert.True(s.ShowsExport);
        var p = await Open(shell.Context, "PURC");
        Assert.False(p.ShowsKind);
        Assert.True(p.ShowsReverseCharge);
    }

    [Fact]
    public async Task Sale_saves_and_shows_the_number()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await Party(shell, "Ravi Stores", "27");
        var s = await Open(shell.Context, "SALE");
        s.Party.Set(party);
        FillLine(s.Lines[0], Sales(shell.Context), "1,000");
        Assert.Equal("Taxable 1,000.00  CGST 90.00  SGST 90.00  Total 1,180.00", s.Totals);
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.Equal("Saved INV/26-27/00001.", shell.Status.Message);
        var edit = await Open(shell.Context, "SALE INV/26-27/00001");
        Assert.Equal(("Ravi Stores", "1,000.00", "18"), (edit.Party.Value!.Name, edit.Lines[0].Price, edit.Lines[0].Rate));
    }

    [Fact]
    public async Task Credit_note_suggests_the_original_invoice_first()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await Party(shell, "Ravi Stores", "27");
        var first = await Open(shell.Context, "SALE");
        first.Party.Set(party);
        FillLine(first.Lines[0], Sales(shell.Context), "1000", "0");
        await first.SaveAsync();
        first.Party.Set(party);
        FillLine(first.Lines[0], Sales(shell.Context), "1000", "0");
        first.Date = "02-06-2026";
        await first.SaveAsync();
        var note = await Open(shell.Context, "CRNT");
        note.Party.Set(party);
        note.OriginalNumber = "INV/26-27/00002";
        FillLine(note.Lines[0], Sales(shell.Context), "300", "0");
        await note.SuggestBillsAsync();
        Assert.Equal(30000, note.Allocations.Single().Paise);
        var bills = await t.OtherOwnerClient().Vouchers.OpenBillsAsync(new OpenBillsRequest { Company = shell.Context.Company!.Id, Party = party.Id }, t.Headers());
        Assert.Equal(bills.Bills.Single(b => b.Number == "INV/26-27/00002").Id, note.Allocations.Single().BillId);
        await note.SaveAsync();
        Assert.Null(note.Message);
    }

    [Fact]
    public async Task Received_credit_note_matches_the_suppliers_invoice_number()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var supplier = await Party(shell, "Pune Steel", "27", "TradePayables");
        var purchases = shell.Context.Lookups.Ledgers(LedgerUse.Any).Single(l => l.Required == "Purchases");
        var p = await Open(shell.Context, "PURC");
        p.Party.Set(supplier);
        p.PartyDocNumber = "PS-101";
        FillLine(p.Lines[0], purchases, "1000", "0");
        await p.SaveAsync();
        p.Party.Set(supplier);
        p.PartyDocNumber = "PS-102";
        FillLine(p.Lines[0], purchases, "1000", "0");
        await p.SaveAsync();
        var note = await Open(shell.Context, "CRNT");
        note.Direction = "Received";
        note.Party.Set(supplier);
        note.OriginalNumber = "ps-102";
        FillLine(note.Lines[0], purchases, "200", "0");
        await note.SuggestBillsAsync();
        var bills = await t.OtherOwnerClient().Vouchers.OpenBillsAsync(new OpenBillsRequest { Company = shell.Context.Company!.Id, Party = supplier.Id }, t.Headers());
        Assert.Equal(bills.Bills.Single(b => b.PartyDocNumber == "PS-102").Id, note.Allocations.Single().BillId);
    }

    [Fact]
    public async Task Unknown_party_opens_PRTY_and_returns_with_it()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Open(shell.Context, "SALE");
        s.Party.Text = "Blr Mart";
        Assert.False(s.Party.Commit());
        await s.CreatePartyAsync();
        var sub = Assert.IsType<PartiesViewModel>(s.SubScreen);
        sub.FormState = sub.States.Single(x => x.Code == "29");
        await sub.SaveAsync();
        Assert.Null(s.SubScreen);
        Assert.Equal("Blr Mart", s.Party.Value!.Name);
        Assert.Equal("29", s.PlaceOfSupply!.Code);
    }

    [Fact]
    public async Task Unregistered_company_sale_hides_tax_fields_and_saves_at_rate_0()
    {
        var shell = await t.LoggedInShellAsync([], company: null);
        var id = (await shell.Context.Host.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = "Small Shop", State = "27", Registration = "Unregistered", Address = "Pune", FirstFyStartYear = 2026,
        }, o))).Company;
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = id, Name = "Small Shop" });
        var party = await Party(shell, "Ravi Stores", "27");
        var s = await Open(shell.Context, "SALE");
        Assert.False(s.ShowsTax);
        s.Party.Set(party);
        FillLine(s.Lines[0], Sales(shell.Context), "1000", "");
        await s.SaveAsync();
        Assert.Null(s.Message);
        Assert.Equal("Saved INV/26-27/00001.", shell.Status.Message);
    }

    [Fact]
    public async Task Opens_with_at_most_three_calls()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var before = t.Requests.Count;
        await Open(shell.Context, "SALE");
        Assert.True(t.Requests.Count - before <= 3);
    }
    [Fact]
    public async Task A_note_changed_after_its_suggestion_is_allocated_again_on_save()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var party = await Party(shell, "Ravi Stores", "27");
        var sale = await Open(shell.Context, "SALE");
        sale.Party.Set(party);
        FillLine(sale.Lines[0], Sales(shell.Context), "1000", "0");
        await sale.SaveAsync();
        var note = await Open(shell.Context, "CRNT");
        note.Party.Set(party);
        FillLine(note.Lines[0], Sales(shell.Context), "300", "0");
        await note.SuggestBillsAsync();
        note.Lines[0].Quantity = "0.5";
        await note.SaveAsync();
        Assert.Null(note.Message);
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = shell.Context.Company!.Id, Type = "CreditNote", Number = "CN/26-27/00001" }, t.Headers());
        Assert.Equal(150_00, v.Allocations.Single().Paise);
    }

    [Theory]
    [InlineData("Regular", "PURC", "04", "04", "18", false)]
    [InlineData("Regular", "SALE", "29", "29", "12", false)]
    [InlineData("Composition", "SALE", "27", "27", "18", false)]
    [InlineData("Composition", "PURC", "27", "27", "18", true)]
    public async Task Screen_totals_equal_the_stored_voucher(string registration, string code, string partyState, string pos, string rate, bool reverseCharge)
    {
        var shell = await t.LoggedInShellAsync([], company: null);
        var id = (await shell.Context.Host.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = "Co", State = "27", Registration = registration, Gstin = "27AAPFU0939F1ZV", Address = "Pune", FirstFyStartYear = 2026,
        }, o))).Company;
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = id, Name = "Co" });
        var party = await Party(shell, "P", partyState, code == "SALE" ? "TradeReceivables" : "TradePayables");
        var s = await Open(shell.Context, code);
        s.Party.Set(party);
        s.PlaceOfSupply = s.States.Single(x => x.Code == pos);
        s.ReverseCharge = reverseCharge;
        var ledger = shell.Context.Lookups.Ledgers(LedgerUse.Any).Single(l => l.Required == (code == "SALE" ? "Sales" : "Purchases"));
        FillLine(s.Lines[0], ledger, "999.99", rate);
        s.Lines[0].Quantity = "3";
        var shown = s.TotalsView;
        Assert.Null(shown.Problem);
        await s.SaveAsync();
        Assert.Null(s.Message);
        var number = shell.Status.Message!["Saved ".Length..].TrimEnd('.');
        var v = await t.OtherOwnerClient().Vouchers.GetByNumberAsync(new VoucherNumberRef { Company = id, Type = code == "SALE" ? "Sale" : "Purchase", Number = number }, t.Headers());
        var lines = v.Invoice.Lines;
        Assert.Equal((lines.Sum(l => l.TaxablePaise), lines.Sum(l => l.CgstPaise), lines.Sum(l => l.SgstPaise), lines.Sum(l => l.UtgstPaise), lines.Sum(l => l.IgstPaise), v.Invoice.RoundOffPaise),
            (shown.Taxable.Paise, shown.Cgst.Paise, shown.Sgst.Paise, shown.Utgst.Paise, shown.Igst.Paise, shown.RoundOff.Paise));
        Assert.Equal(Math.Abs(v.Postings.Single(p => p.Ledger == party.Id).Paise), shown.Total.Paise);
    }
}
