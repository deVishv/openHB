using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Lookups;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Tests.Lookups;

public sealed class LookupCacheTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static int Group(ChartMsg chart, string kind) => chart.Groups.Single(g => g.Kind == kind).Id;

    static async Task AddParty(HostClients h, Grpc.Core.Metadata headers, int company, string name, int group) =>
        await h.Masters.SavePartyAsync(new SavePartyRequest { Company = company, Name = name, Group = group, Party = new PartyMsg { State = "27" } }, headers);

    [Fact]
    public async Task Cache_loads_all_parties_across_pages()
    {
        // No shell, so no change watcher reloads a cache of its own while the parties are added.
        var connection = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(connection);
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = company }, t.Headers());
        var group = Group(chart, "TradeReceivables");
        for (var i = 0; i < 1500; i++) await AddParty(other, t.Headers(), company, $"Party {i:D4}", group);
        var cache = new LookupCache(connection);
        var before = t.Requests.Count(r => r.Path.EndsWith("ListParties", StringComparison.Ordinal));
        await cache.LoadAsync(company);
        Assert.Equal(1500, cache.Parties.Count);
        Assert.Equal(2, t.Requests.Count(r => r.Path.EndsWith("ListParties", StringComparison.Ordinal)) - before);
        Assert.Equal("A Traders", cache.Profile!.Name);
        Assert.Equal(company, cache.Company);
    }

    [Fact]
    public async Task Opening_a_company_loads_its_cache()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var cache = shell.Context.Lookups;
        Assert.Equal(shell.Context.Company!.Id, cache.Company);
        Assert.NotEmpty(cache.Chart.Ledgers);
        Assert.Equal("true", cache.Ledgers(LedgerUse.Any).Count > 0 ? "true" : "false");
    }

    [Fact]
    public async Task Masters_change_reloads_parties()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var company = shell.Context.Company!.Id;
        var cache = shell.Context.Lookups;
        var changed = 0;
        cache.Changed += () => changed++;
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = company }, t.Headers());
        await AddParty(other, t.Headers(), company, "Ravi Stores", Group(chart, "TradeReceivables"));
        await t.Eventually(() => cache.Ledgers(LedgerUse.Party).Any(l => l.Name == "Ravi Stores"));
        Assert.True(changed > 0);
        Assert.True(cache.Ledgers(LedgerUse.Party).Single().IsParty);
    }

    [Fact]
    public async Task Changes_during_a_reload_cause_one_more_reload_not_one_each()
    {
        var connection = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(connection);
        var cache = new LookupCache(connection);
        await cache.LoadAsync(company);
        var before = t.Requests.Count(r => r.Path.EndsWith("GetChart", StringComparison.Ordinal));
        var change = new ChangeEventMsg { Company = company, Kind = "Masters" };
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => cache.OnChangeAsync(change)));
        Assert.InRange(t.Requests.Count(r => r.Path.EndsWith("GetChart", StringComparison.Ordinal)) - before, 1, 2);
    }

    [Fact]
    public async Task Other_company_changes_are_ignored()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var cache = shell.Context.Lookups;
        var otherCompany = await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = otherCompany }, t.Headers());
        var before = t.Requests.Count(r => r.Path.EndsWith("ListParties", StringComparison.Ordinal));
        await AddParty(other, t.Headers(), otherCompany, "Elsewhere", Group(chart, "TradeReceivables"));
        await cache.OnChangeAsync(new ChangeEventMsg { Company = otherCompany, Kind = "Masters" });
        Assert.Equal(before, t.Requests.Count(r => r.Path.EndsWith("ListParties", StringComparison.Ordinal)));
        Assert.DoesNotContain(cache.Ledgers(LedgerUse.Party), l => l.Name == "Elsewhere");
    }

    [Fact]
    public async Task Ledgers_for_contra_are_only_cash_and_bank()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var company = shell.Context.Company!.Id;
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = company }, t.Headers());
        await other.Masters.AddLedgerAsync(new AddLedgerRequest { Company = company, Name = "HDFC Bank", Group = Group(chart, "BankAccounts") }, t.Headers());
        await shell.Context.Lookups.LoadAsync(company);
        var cashOrBank = shell.Context.Lookups.Ledgers(LedgerUse.CashOrBank);
        Assert.Contains(cashOrBank, l => l.Name == "HDFC Bank");
        Assert.Contains(cashOrBank, l => l.Required == "Cash");
        Assert.All(cashOrBank, l => Assert.True(l.IsCashOrBank));
        Assert.DoesNotContain(cashOrBank, l => l.Required == "Sales");
        Assert.Contains(shell.Context.Lookups.Ledgers(LedgerUse.SalesLine), l => l.Required == "Sales");
    }

    [Fact]
    public async Task Logging_out_clears_the_cache()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        await shell.LogOutAsync();
        Assert.Null(shell.Context.Lookups.Company);
        Assert.Empty(shell.Context.Lookups.Chart.Ledgers);
    }
    [Fact]
    public async Task A_failed_load_never_shows_the_previous_companys_lookups()
    {
        var connection = await t.LoggedInOwnerAsync();
        var a = await t.NewCompanyAsync(connection, "A Traders");
        var b = await t.NewCompanyAsync(connection, "B Traders");
        var other = t.OtherOwnerClient();
        var chart = await other.Masters.GetChartAsync(new CompanyRef { Company = a }, t.Headers());
        await AddParty(other, t.Headers(), a, "Only In A", Group(chart, "TradeReceivables"));
        var cache = new LookupCache(connection);
        await cache.LoadAsync(a);
        Assert.NotEmpty(cache.Parties);

        t.FailRequest = ("ListParties", 1);
        await Assert.ThrowsAsync<HostException>(() => cache.LoadAsync(b));
        Assert.Equal(b, cache.Company);
        Assert.Empty(cache.Parties);
        Assert.Empty(cache.Chart.Ledgers);
        Assert.Empty(cache.Ledgers(LedgerUse.Any));
        Assert.Null(cache.Profile);
        await t.Settle(connection);
    }

    [Fact]
    public async Task Opening_a_company_whose_lookups_fail_leaves_an_empty_cache()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        Assert.NotEmpty(shell.Context.Lookups.Chart.Ledgers);
        var b = await t.NewCompanyAsync(shell.Context.Host, "B Traders");
        t.FailRequest = ("GetChart", 1);
        await CompanyOpener.OpenAsync(shell.Context, new CompanyEntry { Id = b, Name = "B Traders" });
        Assert.Equal(b, shell.Context.Company!.Id);
        Assert.Empty(shell.Context.Lookups.Chart.Ledgers);
        await t.Settle(shell.Context.Host);
    }

    [Fact]
    public async Task A_failed_reload_is_tried_again_on_the_next_change()
    {
        var connection = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(connection);
        var cache = new LookupCache(connection);
        await cache.LoadAsync(company);
        t.FailRequest = ("GetChart", 1);
        await Assert.ThrowsAsync<HostException>(() => cache.OnChangeAsync(new ChangeEventMsg { Company = company, Kind = "Masters" }));
        t.FailRequest = null;
        for (var i = 0; i < 6 && connection.State != ConnectionState.Connected; i++)
        {
            await t.Settle(connection);
            t.Time.Advance(TimeSpan.FromSeconds(2));
            await t.Settle(connection);
        }
        Assert.Equal(ConnectionState.Connected, connection.State);
        var before = t.Requests.Count(r => r.Path.EndsWith("GetChart", StringComparison.Ordinal));
        await cache.OnChangeAsync(new ChangeEventMsg { Company = company, Kind = "Settings" });
        Assert.Equal(before + 1, t.Requests.Count(r => r.Path.EndsWith("GetChart", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Clear_drops_a_pending_reload()
    {
        var connection = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(connection);
        var cache = new LookupCache(connection);
        await cache.LoadAsync(company);
        var reload = cache.OnChangeAsync(new ChangeEventMsg { Company = company, Kind = "Masters" });
        cache.Clear();
        await reload;
        Assert.Null(cache.Company);
        Assert.Empty(cache.Chart.Ledgers);
    }

    [Fact]
    public void A_group_cycle_does_not_hang()
    {
        var chart = new ChartMsg
        {
            Groups = { new GroupMsg { Id = 1, Name = "A", Parent = 2, Kind = "None", Nature = "Asset" }, new GroupMsg { Id = 2, Name = "B", Parent = 1, Kind = "None", Nature = "Asset" } },
            Ledgers = { new LedgerMsg { Id = 9, Name = "Loop", Group = 1 } },
        };
        var options = LookupCache.Options(chart, new Dictionary<int, PartyMsg>());
        Assert.False(options.Single().IsCashOrBank);
    }
    [Fact]
    public async Task A_failed_refresh_keeps_what_was_loaded()
    {
        var connection = await t.LoggedInOwnerAsync();
        var company = await t.NewCompanyAsync(connection);
        var cache = new LookupCache(connection);
        await cache.LoadAsync(company);
        var ledgers = cache.Ledgers(LedgerUse.Any).Count;
        t.FailRequest = ("GetChart", 1);
        await Assert.ThrowsAsync<HostException>(() => cache.RefreshAsync());
        Assert.Equal(ledgers, cache.Ledgers(LedgerUse.Any).Count);
        Assert.Equal(company, cache.Company);
        await t.Settle(connection);
    }
}
