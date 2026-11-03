using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Printing;
using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Shell;

/// <summary>PRNT prints the current tab, PRNT pdf saves it as PDF; F11 and Ctrl+Shift+E run them (spec 6.1, 6.3).</summary>
public sealed class PrintCommandTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static readonly IReadOnlySet<string> NoContext = new HashSet<string>();

    Task<ShellViewModel> Shell() => t.LoggedInShellAsync([], company: "A Traders", built: ScreenCatalog.Built());

    static KeyChord Chord(string key) => KeyChord.TryParse(key, out var c) ? c : throw new ArgumentException(key);

    static List<(PrintDocument Doc, string File, PrintTarget Target)> Capture(ShellViewModel shell)
    {
        var seen = new List<(PrintDocument, string, PrintTarget)>();
        shell.PrintRequested += (doc, file, target) => seen.Add((doc, file, target));
        return seen;
    }

    async Task<string> Sale(HostConnection host, int company, string party)
    {
        var chart = await host.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o));
        var group = chart.Groups.Single(g => g.Kind == "TradeReceivables").Id;
        var id = (int)(await host.CallAsync((h, o) => h.Masters.SavePartyAsync(new SavePartyRequest
        {
            Company = company, Name = party, Group = group, Party = new PartyMsg { State = "27", Address = "Pune" },
        }, o))).Id;
        var sales = chart.Ledgers.Single(l => l.Required == "Sales").Id;
        return (await host.CallAsync((h, o) => h.Vouchers.SaveInvoiceAsync(new InvoiceRequest
        {
            Company = company,
            Invoice = new InvoiceMsg
            {
                Type = "Sale", Date = "2026-06-01", Party = id, Direction = "Outward", Kind = "Regular", PlaceOfSupply = "27",
                Lines = { new InvoiceLineMsg { Ledger = sales, Description = "Rod", HsnSac = "7214", QtyThousandths = 1000, UnitPricePaise = 100_00, RateBp = 1800 } },
            },
        }, o))).DisplayNumber;
    }

    [Fact]
    public async Task Prnt_with_no_tab_or_an_unprintable_tab_says_nothing_to_print()
    {
        var shell = await Shell();
        var seen = Capture(shell);
        await shell.RunAsync("PRNT", newTab: false);
        Assert.Equal("Nothing to print in this tab.", shell.Status.Message);
        await shell.RunAsync("HELP", newTab: true);
        await shell.RunAsync("PRNT pdf", newTab: false);
        Assert.Equal("Nothing to print in this tab.", shell.Status.Message);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Prnt_pdf_raises_a_pdf_request_with_the_file_name()
    {
        var shell = await Shell();
        var seen = Capture(shell);
        await shell.RunAsync("TRLB", newTab: true);
        await shell.RunAsync("prnt PDF", newTab: false);
        var (doc, file, target) = Assert.Single(seen);
        Assert.Equal(PrintTarget.Pdf, target);
        Assert.Equal("TRLB 2026-04-01 2026-06-01.pdf", file);
        Assert.Equal("Trial balance, 01-04-2026 to 01-06-2026", doc.Title);
        Assert.Equal("", shell.Prompt.Text);
        shell.Printed("Saved TRLB.pdf.");
        Assert.Equal("Saved TRLB.pdf.", shell.Status.Message);
    }

    [Fact]
    public async Task F11_and_ctrl_shift_e_run_print_and_pdf()
    {
        var shell = await Shell();
        var seen = Capture(shell);
        await shell.RunAsync("TRLB", newTab: true);
        Assert.True(await shell.HandleKeyAsync(Chord("f11"), NoContext));
        Assert.True(await shell.HandleKeyAsync(Chord("ctrl+shift+e"), NoContext));
        Assert.Equal([PrintTarget.Paper, PrintTarget.Pdf], seen.Select(s => s.Target));
        Assert.Contains("screen.savePdf", KeyCommands.All);
    }

    [Fact]
    public async Task Prnt_adds_no_tab_and_no_back_step()
    {
        var shell = await Shell();
        await shell.RunAsync("TRLB", newTab: true);
        var tab = shell.Tabs.Active!;
        var screen = tab.Screen;
        await shell.RunAsync("PRNT", newTab: false);
        await shell.RunAsync("PRNT", newTab: true);
        Assert.Single(shell.Tabs.Tabs);
        Assert.Same(screen, shell.Tabs.Active!.Screen);
        Assert.Equal("TRLB", tab.Command);
        Assert.False(tab.CanGoBack);
    }

    [Fact]
    public async Task Prnt_with_other_words_says_how()
    {
        var shell = await Shell();
        var seen = Capture(shell);
        await shell.RunAsync("TRLB", newTab: true);
        await shell.RunAsync("PRNT word", newTab: false);
        Assert.Equal("Type PRNT, or PRNT pdf.", shell.Status.Message);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task Data_entry_prints_their_own_invoice()
    {
        var owner = await Shell();
        var company = owner.Context.Company!.Id;
        var clerk = await t.UserShellAsync("clerk", "DataEntry", company);
        await CompanyOpener.OpenAsync(clerk.Context, new CompanyEntry { Id = company, Name = "A Traders" });
        var number = await Sale(clerk.Context.Host, company, "Ravi Stores");
        await t.Eventually(() => clerk.Context.Lookups.Ledgers(Model.Lookups.LedgerUse.Party).Any(l => l.Name == "Ravi Stores"));
        // The clerk's test shell has fake screens only, so the real ones are made in the clerk's context.
        async Task<IPrintable> Open(string command)
        {
            Assert.True(Core.Commands.CommandParser.TryParse(command, out var cmd));
            var screen = (await ScreenCatalog.Built()[cmd.Code](cmd, clerk.Context))!;
            await screen.LoadAsync(CancellationToken.None);
            return Assert.IsAssignableFrom<IPrintable>(screen);
        }
        var invoice = await (await Open($"SALE {number}")).PrintAsync(PrintTarget.Paper);
        Assert.Null(invoice.Refusal);
        Assert.NotNull(invoice.Document);
        var report = await (await Open("TRLB")).PrintAsync(PrintTarget.Paper);
        Assert.Equal("Printing reports needs the export right.", report.Refusal);
    }

    [Fact]
    public async Task Pdf_with_devanagari_is_refused_with_a_hint()
    {
        var shell = await Shell();
        var number = await Sale(shell.Context.Host, shell.Context.Company!.Id, "राम Traders");
        await t.Eventually(() => shell.Context.Lookups.Ledgers(Model.Lookups.LedgerUse.Party).Any(l => l.Name == "राम Traders"));
        var seen = Capture(shell);
        await shell.RunAsync($"SALE {number}", newTab: true);
        await shell.RunAsync("PRNT pdf", newTab: false);
        Assert.Equal("The PDF cannot show \"राम Traders\". Print it and choose Microsoft Print to PDF.", shell.Status.Message);
        Assert.Empty(seen);
        await shell.RunAsync("PRNT", newTab: false);
        Assert.Equal(PrintTarget.Paper, Assert.Single(seen).Target);
    }
}
