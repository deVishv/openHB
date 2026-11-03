using Google.Protobuf;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Screens.Entry;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Gst;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Tests.Printing;

/// <summary>Printed GST documents (Rules 46, 48, 49, 53): the stored figures, the particulars, and the copies.</summary>
public sealed class InvoicePrintTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static int Required(ShellViewModel shell, string required) => shell.Context.Lookups.Chart.Ledgers.Single(l => l.Required == required).Id;

    async Task<int> Party(ShellViewModel shell, string name, string state, string gstin = "", string address = "")
    {
        var group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var id = (int)(await t.OtherOwnerClient().Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = shell.Context.Company!.Id, Name = name, Group = group, Party = new PartyMsg { State = state, Gstin = gstin, Address = address },
        }, t.Headers())).Id;
        await t.Eventually(() => shell.Context.Lookups.Parties.ContainsKey(id));
        return id;
    }

    static InvoiceLineMsg Line(ShellViewModel shell, long price, int rateBp = 1800, string hsn = "7214", long qty = 1000, string description = "Steel rod") =>
        new() { Ledger = Required(shell, "Sales"), Description = description, HsnSac = hsn, QtyThousandths = qty, UnitPricePaise = price, RateBp = rateBp };

    async Task<SavedReply> Save(ShellViewModel shell, int party, string pos, params InvoiceLineMsg[] lines) =>
        await Save(shell, new InvoiceMsg { Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = pos, Lines = { lines } });

    async Task<SavedReply> Save(ShellViewModel shell, InvoiceMsg invoice) =>
        await t.OtherOwnerClient().Vouchers.SaveInvoiceAsync(new InvoiceRequest { Company = shell.Context.Company!.Id, Invoice = invoice }, t.Headers());

    static async Task<InvoiceEntryViewModel> Open(ShellViewModel shell, string command)
    {
        await shell.RunAsync(command, newTab: true);
        var s = Assert.IsType<InvoiceEntryViewModel>(shell.Tabs.Active!.Screen);
        Assert.Null(s.Message);
        return s;
    }

    static async Task<PrintDocument> Print(ShellViewModel shell, string command, PrintTarget target = PrintTarget.Pdf)
    {
        var result = await (await Open(shell, command)).PrintAsync(target);
        Assert.Null(result.Refusal);
        return result.Document!;
    }

    static List<string> Texts(PrintDocument d) => [.. d.Pages.SelectMany(p => p.Items).OfType<PrintText>().Select(x => x.Text)];

    static List<string> Texts(PrintPage p) => [.. p.Items.OfType<PrintText>().Select(x => x.Text)];

    async Task<long> PartyPosting(ShellViewModel shell, long voucher, int party) =>
        (await t.OtherOwnerClient().Vouchers.GetAsync(new VoucherRef { Company = shell.Context.Company!.Id, Id = voucher }, t.Headers()))
        .Postings.Single(p => p.Ledger == party).Paise;

    async Task OpenCompany(ShellViewModel shell, string name, string state, string registration, string gstin)
    {
        var id = (await shell.Context.Host.CallAsync((h, o) => h.Companies.CreateAsync(new CompanyProfileMsg
        {
            Name = name, State = state, Registration = registration, Gstin = gstin, Address = "Plot 1, Sector 17", FirstFyStartYear = 2026,
        }, o))).Company;
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = id, Name = name });
    }

    async Task SetSetting(ShellViewModel shell, string key, string json)
    {
        await t.OtherOwnerClient().Companies.SetSettingAsync(new SettingRequest { Company = shell.Context.Company!.Id, Key = key, Json = json }, t.Headers());
        await shell.Context.Settings.LoadAsync(shell.Context.Company!.Id);
    }

    [Fact]
    public async Task Intra_state_invoice_shows_cgst_sgst_and_totals_equal_the_party_posting()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "4 Station Road, Pune");
        var saved = await Save(shell, party, "27", Line(shell, 1000_00, qty: 2000));
        var doc = await Print(shell, "SALE INV/26-27/00001");
        var texts = Texts(doc);
        Assert.Equal("Tax invoice", doc.Title.Split(' ')[0] + " " + doc.Title.Split(' ')[1]);
        Assert.Contains("Tax invoice", texts);
        Assert.Contains("Invoice no. INV/26-27/00001", texts);
        Assert.Contains("Date 01-06-2026", texts);
        Assert.Contains("Place of supply Maharashtra (27)", texts);
        Assert.Contains("Reverse charge No", texts);
        Assert.Contains("GSTIN 27AAPFU0939F1ZV", texts);
        Assert.Contains("GSTIN 27AAACR5055K1Z7", texts);
        Assert.Contains("Bill to", texts);
        Assert.Contains("ORIGINAL FOR RECIPIENT", texts);
        Assert.Contains("CGST %", texts);
        Assert.Contains("SGST %", texts);
        Assert.DoesNotContain("IGST %", texts);
        Assert.Equal(2, texts.Count(x => x == "9%"));
        Assert.Contains("2", texts);
        Assert.Equal(2360_00, await PartyPosting(shell, saved.Id, party));
        Assert.Contains("2,360.00", texts);
        Assert.Contains("2,000.00", texts);
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Inter_state_invoice_shows_igst()
    {
        var shell = await Shell();
        var party = await Party(shell, "Blr Mart", "29", "29AABCB1234C1ZA", "MG Road, Bengaluru");
        await Save(shell, party, "29", Line(shell, 1000_00));
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("Place of supply Karnataka (29)", texts);
        Assert.Contains("IGST %", texts);
        Assert.Contains("18%", texts);
        Assert.Contains("180.00", texts);
        Assert.DoesNotContain("CGST %", texts);
    }

    [Fact]
    public async Task Union_territory_invoice_shows_utgst()
    {
        var shell = await Shell();
        await OpenCompany(shell, "C Traders", "04", "Regular", "04AAPFU0939F1Z3");
        var party = await Party(shell, "Sector Mart", "04", address: "Sector 22, Chandigarh");
        await Save(shell, party, "04", Line(shell, 1000_00));
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("UTGST %", texts);
        Assert.Contains("CGST %", texts);
        Assert.DoesNotContain("SGST %", texts);
        Assert.Contains("State Chandigarh (04)", texts);
    }

    [Fact]
    public async Task Export_under_lut_shows_endorsement_port_and_country()
    {
        var shell = await Shell();
        var party = await Party(shell, "Gulf LLC", "96", address: "Deira, Dubai");
        await Save(shell, new InvoiceMsg
        {
            Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "ExportUnderLut", PlaceOfSupply = "96",
            PortCode = "INNSA1", ShippingBillNumber = "1234567", ShippingBillDate = "2026-06-02",
            ShipTo = new ShipToMsg { Name = "Gulf LLC Warehouse", Address = "Jebel Ali", Country = "United Arab Emirates" },
            Lines = { Line(shell, 1000_00) },
        });
        var doc = await Print(shell, "SALE INV/26-27/00001");
        var texts = Texts(doc);
        Assert.Contains("SUPPLY MEANT FOR EXPORT UNDER BOND OR LETTER OF UNDERTAKING WITHOUT PAYMENT OF INTEGRATED TAX", texts);
        Assert.Contains("Port code INNSA1", texts);
        Assert.Contains("Shipping bill 1234567, 02-06-2026", texts);
        Assert.Contains("Ship to", texts);
        Assert.Contains("Gulf LLC Warehouse", texts);
        Assert.Contains("Country United Arab Emirates", texts);
        Assert.Contains("IGST %", texts);
        Assert.Contains("Tax invoice", texts);
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Round_off_and_tax_inclusive_prices_use_stored_figures()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        var line = Line(shell, 999_99);
        line.PriceIncludesTax = true;
        var saved = await Save(shell, party, "27", line, Line(shell, 10_33, rateBp: 500));
        var stored = (await t.OtherOwnerClient().Vouchers.GetAsync(new VoucherRef { Company = shell.Context.Company!.Id, Id = saved.Id }, t.Headers())).Invoice;
        Assert.NotEqual(0, stored.RoundOffPaise);
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        string Rupees(long paise) => Core.Formatting.IndianNumberFormat.Format(new Core.Amounts.Money(paise), Core.Formatting.NegativeStyle.Minus);
        foreach (var l in stored.Lines) Assert.Contains(Rupees(l.TaxablePaise), texts);
        Assert.Contains(Rupees(stored.Lines.Sum(l => l.TaxablePaise)), texts);
        Assert.Contains(Rupees(stored.RoundOffPaise), texts);
        Assert.Contains("Round off", texts);
        Assert.Contains(Rupees(await PartyPosting(shell, saved.Id, party)), texts);
    }

    [Fact]
    public async Task Every_nonzero_stored_tax_appears_in_a_line_column()
    {
        var shell = await Shell();
        var party = await Party(shell, "Blr Mart", "29", "29AABCB1234C1ZA", "Bengaluru");
        var saved = await Save(shell, party, "29", Line(shell, 1000_00));
        var stored = await t.OtherOwnerClient().Vouchers.GetAsync(new VoucherRef { Company = shell.Context.Company!.Id, Id = saved.Id }, t.Headers());
        // As if the company's state had moved to the buyer's: the stored IGST must still print under IGST.
        stored.Invoice.CompanyDetails.State = "29";
        var input = InvoicePrintInput.From(stored, shell.Context.Lookups, shell.Context.Settings.Current, null);
        var texts = Texts(InvoicePrint.Build(input, ["ORIGINAL FOR RECIPIENT"], GstData.LoadEmbedded(), PdfMeasure.Instance));
        Assert.Contains("IGST %", texts);
        Assert.Contains("180.00", texts);
        Assert.DoesNotContain("CGST %", texts);
    }

    [Fact]
    public async Task Composition_company_prints_bill_of_supply_without_tax_columns()
    {
        var shell = await Shell();
        await OpenCompany(shell, "D Traders", "27", "Composition", "27AAPFU0939F1ZV");
        var party = await Party(shell, "Ravi Stores", "27", address: "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("Bill of supply", texts);
        Assert.Contains("Composition taxable person, not eligible to collect tax on supplies", texts);
        Assert.DoesNotContain("CGST %", texts);
        Assert.DoesNotContain("IGST %", texts);
        Assert.DoesNotContain("Tax invoice", texts);
    }

    [Fact]
    public async Task Unregistered_company_prints_invoice_without_gstin()
    {
        var shell = await Shell();
        await OpenCompany(shell, "E Stores", "27", "Unregistered", "");
        var party = await Party(shell, "Walk in", "27");
        await Save(shell, party, "27", Line(shell, 75000_00, rateBp: 0, description: "Steel rod"));
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("Invoice", texts);
        Assert.DoesNotContain(texts, x => x.StartsWith("GSTIN", StringComparison.Ordinal));
        Assert.DoesNotContain("CGST %", texts);
    }

    [Fact]
    public async Task Credit_note_shows_original_invoice()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        await Save(shell, new InvoiceMsg
        {
            Type = "CreditNote", Date = "2026-06-05", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
            OriginalNumber = "INV/26-27/00001", OriginalDate = "2026-06-01", Lines = { Line(shell, 100_00) },
        });
        var texts = Texts(await Print(shell, "CRNT CN/26-27/00001"));
        Assert.Contains("Credit note", texts);
        Assert.Contains("Note no. CN/26-27/00001", texts);
        Assert.Contains("Original invoice INV/26-27/00001, 01-06-2026", texts);
    }

    [Fact]
    public async Task Reprint_shows_details_as_saved_after_party_changes()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "4 Station Road, Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        var client = t.OtherOwnerClient();
        var current = shell.Context.Lookups.Parties[party];
        await client.Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = shell.Context.Company!.Id, Name = "Ravi Stores", Group = shell.Context.Lookups.Chart.Groups.Single(g => g.Kind == "TradeReceivables").Id,
            Party = new PartyMsg { Ledger = party, State = "27", Gstin = "27AAACR5055K1Z7", Address = "New Market, Pune", RowVersion = current.RowVersion },
        }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.Parties[party].Address == "New Market, Pune");
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("4 Station Road, Pune", texts);
        Assert.DoesNotContain("New Market, Pune", texts);
    }

    [Fact]
    public async Task Invoice_saved_before_0004_uses_the_masters()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "4 Station Road, Pune");
        var saved = await Save(shell, party, "27", Line(shell, 1000_00));
        var stored = await t.OtherOwnerClient().Vouchers.GetAsync(new VoucherRef { Company = shell.Context.Company!.Id, Id = saved.Id }, t.Headers());
        stored.Invoice.PartyDetails = null;
        stored.Invoice.CompanyDetails = null;
        var input = InvoicePrintInput.From(stored, shell.Context.Lookups, shell.Context.Settings.Current, null);
        Assert.Equal(new DocumentParty("Ravi Stores", "27AAACR5055K1Z7", "4 Station Road, Pune", new StateCode("27")), input.Buyer);
        Assert.Equal(new DocumentParty("A Traders", "27AAPFU0939F1ZV", "Pune", new StateCode("27")), input.Company);
    }

    [Fact]
    public async Task Optional_columns_hide_price_and_discount_but_never_hsn_or_quantity()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        var before = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.Contains("Rate", before);
        Assert.Contains("Discount", before);
        await SetSetting(shell, "invoice.showPrice", "false");
        await SetSetting(shell, "invoice.showDiscount", "false");
        var texts = Texts(await Print(shell, "SALE INV/26-27/00001"));
        Assert.DoesNotContain("Rate", texts);
        Assert.DoesNotContain("Discount", texts);
        Assert.Contains("HSN/SAC", texts);
        Assert.Contains("7214", texts);
        Assert.Contains("Qty", texts);
        Assert.Contains("Unit", texts);
        Assert.Contains("CGST %", texts);
        Assert.DoesNotContain(SettingsCatalog.All, d => d.Key.StartsWith("invoice.show", StringComparison.Ordinal)
            && d.Key is not ("invoice.showPrice" or "invoice.showDiscount"));
    }

    [Fact]
    public async Task Goods_print_three_copies_services_two_and_pdf_one()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        await Save(shell, party, "27", Line(shell, 1000_00, hsn: "998314", description: "IT support"));
        var goods = await Print(shell, "SALE INV/26-27/00001", PrintTarget.Paper);
        Assert.Equal(["ORIGINAL FOR RECIPIENT", "DUPLICATE FOR TRANSPORTER", "TRIPLICATE FOR SUPPLIER"],
            goods.Pages.Select(p => Texts(p).First(x => x.EndsWith(" FOR RECIPIENT", StringComparison.Ordinal) || x.EndsWith(" FOR TRANSPORTER", StringComparison.Ordinal) || x.EndsWith(" FOR SUPPLIER", StringComparison.Ordinal))));
        var services = await Print(shell, "SALE INV/26-27/00002", PrintTarget.Paper);
        Assert.Equal(2, services.Pages.Count);
        Assert.Contains("DUPLICATE FOR SUPPLIER", Texts(services.Pages[1]));
        var pdf = await Print(shell, "SALE INV/26-27/00001", PrintTarget.Pdf);
        Assert.Single(pdf.Pages);
        Assert.Contains("ORIGINAL FOR RECIPIENT", Texts(pdf));
        await SetSetting(shell, "invoice.copies", "\"original\"");
        Assert.Single((await Print(shell, "SALE INV/26-27/00001", PrintTarget.Paper)).Pages);
    }

    [Fact]
    public async Task Unregistered_buyer_at_fifty_thousand_without_address_is_refused()
    {
        var shell = await Shell();
        var party = await Party(shell, "Walk in", "27");
        await Save(shell, party, "27", Line(shell, 50000_00));
        var result = await (await Open(shell, "SALE INV/26-27/00001")).PrintAsync(PrintTarget.Paper);
        Assert.Null(result.Document);
        Assert.Equal("Add the buyer's address in PRTY, or a Ship to address, before printing.", result.Refusal);
        await Save(shell, party, "27", Line(shell, 49999_99));
        Assert.NotNull((await (await Open(shell, "SALE INV/26-27/00002")).PrintAsync(PrintTarget.Paper)).Document);
    }

    [Fact]
    public async Task Sixty_lines_continue_on_a_second_page_with_totals_last()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", [.. Enumerable.Range(1, 60).Select(n => Line(shell, 100_00, description: $"Item {n:00}"))]);
        var doc = await Print(shell, "SALE INV/26-27/00001");
        Assert.True(doc.Pages.Count >= 2);
        Assert.Equal(Enumerable.Range(1, 60).Select(n => $"Item {n:00}"), Texts(doc).Where(x => x.StartsWith("Item ", StringComparison.Ordinal)));
        Assert.All(doc.Pages, p => Assert.Contains("Description", Texts(p)));
        Assert.All(doc.Pages, p => Assert.Contains("ORIGINAL FOR RECIPIENT", Texts(p)));
        Assert.Contains("Tax invoice INV/26-27/00001, 01-06-2026 (continued)", Texts(doc.Pages[1]));
        Assert.Contains("Total", Texts(doc.Pages[^1]));
        Assert.All(doc.Pages.SkipLast(1), p => Assert.DoesNotContain("Total", Texts(p)));
        for (var i = 0; i < doc.Pages.Count; i++) Assert.Contains($"Page {i + 1} of {doc.Pages.Count}", Texts(doc.Pages[i]));
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Long_address_wraps_inside_its_block()
    {
        var shell = await Shell();
        var address = string.Join(", ", Enumerable.Repeat("Shop 12, Laxmi Road Market Building", 8));
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", address);
        await Save(shell, party, "27", Line(shell, 1000_00));
        var doc = await Print(shell, "SALE INV/26-27/00001");
        Assert.True(Texts(doc).Count(x => x.Contains("Laxmi Road", StringComparison.Ordinal)) > 3);
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Amount_in_words_matches_total()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00, qty: 2000));
        Assert.Contains("Amount in words: Rupees Two Thousand Three Hundred Sixty Only", Texts(await Print(shell, "SALE INV/26-27/00001")));
    }

    [Fact]
    public async Task Logo_bank_details_and_signatory_print_from_settings()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await Save(shell, party, "27", Line(shell, 1000_00));
        await t.OtherOwnerClient().Companies.SetLogoAsync(new SetLogoRequest { Company = shell.Context.Company!.Id, Png = ByteString.CopyFrom(PdfExportTests.TinyPng) }, t.Headers());
        await t.Eventually(() => shell.Context.Lookups.LogoAsync().GetAwaiter().GetResult() is not null);
        await SetSetting(shell, "invoice.bankDetails", System.Text.Json.JsonSerializer.Serialize("HDFC Bank\nA/c 50100012345"));
        await SetSetting(shell, "invoice.terms", "\"Goods once sold are not taken back.\"");
        await SetSetting(shell, "invoice.signatory", "\"Partner\"");
        var doc = await Print(shell, "SALE INV/26-27/00001");
        var texts = Texts(doc);
        Assert.Single(doc.Pages[0].Items.OfType<PrintImage>());
        Assert.Contains("Bank details", texts);
        Assert.Contains("HDFC Bank", texts);
        Assert.Contains("A/c 50100012345", texts);
        Assert.Contains("Terms", texts);
        Assert.Contains("Goods once sold are not taken back.", texts);
        Assert.Contains("For A Traders", texts);
        Assert.Contains("Partner", texts);
        ReportPrintTests.AssertLaidOut(doc);
    }

    [Fact]
    public async Task Cancelled_invoice_prints_marked_cancelled()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        var saved = await Save(shell, party, "27", Line(shell, 1000_00));
        await t.OtherOwnerClient().Vouchers.CancelAsync(new CancelRequest { Company = shell.Context.Company!.Id, Id = saved.Id, RowVersion = saved.RowVersion, Reason = "wrong party" }, t.Headers());
        Assert.Contains("CANCELLED", Texts(await Print(shell, "SALE INV/26-27/00001")));
    }

    [Fact]
    public async Task Unsaved_invoice_says_save_first()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        await shell.RunAsync("SALE", newTab: true);
        var fresh = Assert.IsType<InvoiceEntryViewModel>(shell.Tabs.Active!.Screen);
        Assert.Equal("Save the voucher before printing.", (await fresh.PrintAsync(PrintTarget.Paper)).Refusal);
        await Save(shell, party, "27", Line(shell, 1000_00));
        var s = await Open(shell, "SALE INV/26-27/00001");
        s.Narration = "changed";
        Assert.True(s.HasUnsavedEdits);
        Assert.Equal("Save the voucher before printing.", (await s.PrintAsync(PrintTarget.Pdf)).Refusal);
    }

    [Fact]
    public async Task Invoice_changed_elsewhere_says_reload()
    {
        var shell = await Shell();
        var party = await Party(shell, "Ravi Stores", "27", "27AAACR5055K1Z7", "Pune");
        var saved = await Save(shell, party, "27", Line(shell, 1000_00));
        var s = await Open(shell, "SALE INV/26-27/00001");
        await t.OtherOwnerClient().Vouchers.EditInvoiceAsync(new EditInvoiceRequest
        {
            Company = shell.Context.Company!.Id, Id = saved.Id, RowVersion = saved.RowVersion,
            Invoice = new InvoiceMsg { Type = "Sale", Date = "2026-06-01", Party = party, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27", Lines = { Line(shell, 2000_00) } },
        }, t.Headers());
        var result = await s.PrintAsync(PrintTarget.Paper);
        Assert.Null(result.Document);
        Assert.Equal("This voucher was changed. Reload it with RFSH, then print.", result.Refusal);
    }
}
