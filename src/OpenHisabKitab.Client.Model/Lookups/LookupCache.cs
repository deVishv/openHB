using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Lookups;

/// <summary>Which ledgers a field offers.</summary>
public enum LedgerUse { Any, CashOrBank, Party, SalesLine, PurchaseLine, NotParty }

/// <summary>A ledger as fields show it. Nature is the group's nature (Asset, Liability, Income, Expense). Required is "" for ordinary ledgers.</summary>
public sealed record LedgerOption(int Id, string Name, string Group, string Nature, bool IsParty, bool IsCashOrBank, string Required);

/// <summary>
/// The open company's lookups, cached on the client and refreshed from the change stream (spec 13, Remote ready client):
/// the chart, the profile and company settings, and every party and item. Loaded when the company opens, so a screen
/// shows them without asking the host.
/// </summary>
public sealed class LookupCache(HostConnection host)
{
    const int PageSize = 1000;
    static readonly HashSet<string> CashKinds = ["CashInHand", "BankAccounts", "BankOverdraft"];

    IReadOnlyList<LedgerOption> ledgers = [];

    // A reload is running; changes that arrive meanwhile ask for one more, so a burst of changes costs two reloads, not one each.
    Task? reloading;
    bool mastersAgain, settingsAgain;

    public int? Company { get; private set; }
    public CompanyProfileMsg? Profile { get; private set; }
    public ChartMsg Chart { get; private set; } = new();
    public IReadOnlyDictionary<int, PartyMsg> Parties { get; private set; } = new Dictionary<int, PartyMsg>();
    public IReadOnlyList<ItemMsg> Items { get; private set; } = [];
    public IReadOnlyDictionary<string, string> CompanySettings { get; private set; } = new Dictionary<string, string>();

    /// <summary>After any reload.</summary>
    public event Action? Changed;

    /// <summary>Chart, profile, company settings, and every page of parties and items.</summary>
    public async Task LoadAsync(int company, CancellationToken ct = default)
    {
        Company = company;
        await LoadMastersAsync(company, ct);
        await LoadSettingsAsync(company, ct);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Company = null;
        Profile = null;
        Chart = new ChartMsg();
        Parties = new Dictionary<int, PartyMsg>();
        Items = [];
        CompanySettings = new Dictionary<string, string>();
        ledgers = [];
        Changed?.Invoke();
    }

    public IReadOnlyList<LedgerOption> Ledgers(LedgerUse use) => use switch
    {
        LedgerUse.Any => ledgers,
        LedgerUse.CashOrBank => [.. ledgers.Where(l => l.IsCashOrBank)],
        LedgerUse.Party => [.. ledgers.Where(l => l.IsParty)],
        LedgerUse.NotParty => [.. ledgers.Where(l => !l.IsParty)],
        // As the host checks invoice lines: income for sales; expense, or an asset other than cash and bank, for purchases.
        LedgerUse.SalesLine => [.. ledgers.Where(l => !l.IsParty && l.Nature == "Income" && l.Required is "" or "Sales")],
        LedgerUse.PurchaseLine => [.. ledgers.Where(l => !l.IsParty && l.Required is "" or "Purchases"
            && (l.Nature == "Expense" || l.Nature == "Asset" && !l.IsCashOrBank))],
        _ => throw new ArgumentOutOfRangeException(nameof(use)),
    };

    public string LedgerName(int id) => ledgers.FirstOrDefault(l => l.Id == id)?.Name ?? "";

    /// <summary>Masters changes reload the chart, parties, and items; settings changes the profile and settings. Other companies are ignored.</summary>
    public Task OnChangeAsync(ChangeEventMsg change)
    {
        if (Company is not { } company || change.Company != company) return Task.CompletedTask;
        if (change.Kind is "Masters" or "Restore") mastersAgain = true;
        if (change.Kind is "Settings" or "Restore") settingsAgain = true;
        if (!mastersAgain && !settingsAgain) return Task.CompletedTask;
        return reloading ??= ReloadAsync(company);
    }

    async Task ReloadAsync(int company)
    {
        try
        {
            while (mastersAgain || settingsAgain)
            {
                var (masters, settings) = (mastersAgain, settingsAgain);
                (mastersAgain, settingsAgain) = (false, false);
                if (masters) await LoadMastersAsync(company, default);
                if (settings) await LoadSettingsAsync(company, default);
                Changed?.Invoke();
            }
        }
        finally { reloading = null; }
    }

    async Task LoadMastersAsync(int company, CancellationToken ct)
    {
        var chart = await host.CallAsync((h, o) => h.Masters.GetChartAsync(new CompanyRef { Company = company }, o), ct);
        var parties = new Dictionary<int, PartyMsg>();
        var after = "";
        do
        {
            var page = await host.CallAsync((h, o) => h.Masters.ListPartiesAsync(Request(company, after), o), ct);
            foreach (var p in page.Parties) parties[p.Ledger] = p;
            after = page.Next;
        } while (after.Length > 0);
        var items = new List<ItemMsg>();
        do
        {
            var page = await host.CallAsync((h, o) => h.Masters.ListItemsAsync(Request(company, after), o), ct);
            items.AddRange(page.Items);
            after = page.Next;
        } while (after.Length > 0);
        if (Company != company) return;
        (Chart, Parties, Items) = (chart, parties, [.. items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)]);
        ledgers = Options(chart, parties);
    }

    async Task LoadSettingsAsync(int company, CancellationToken ct)
    {
        var profile = await host.CallAsync((h, o) => h.Companies.GetAsync(new CompanyRef { Company = company }, o), ct);
        var settings = await host.CallAsync((h, o) => h.Companies.ListSettingsAsync(new CompanyRef { Company = company }, o), ct);
        if (Company != company) return;
        Profile = profile;
        CompanySettings = settings.Values.ToDictionary(v => v.Key, v => v.Json, StringComparer.Ordinal);
    }

    static ListRequest Request(int company, string after) => new() { Company = company, Page = new PageMsg { Limit = PageSize, After = after } };

    static List<LedgerOption> Options(ChartMsg chart, IReadOnlyDictionary<int, PartyMsg> parties)
    {
        var groups = chart.Groups.ToDictionary(g => g.Id);
        bool UnderCash(int id)
        {
            for (var g = id; groups.TryGetValue(g, out var group); g = group.Parent)
                if (CashKinds.Contains(group.Kind)) return true;
            return false;
        }
        return [.. chart.Ledgers
            .Select(l => new LedgerOption(l.Id, l.Name, groups.TryGetValue(l.Group, out var g) ? g.Name : "", groups.TryGetValue(l.Group, out var n) ? n.Nature : "",
                parties.ContainsKey(l.Id), UnderCash(l.Group), l.Required))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
