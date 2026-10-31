using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class ItemsTests : IDisposable
{
    readonly ClientTestHost t = new();

    public void Dispose() => t.Dispose();

    static async Task<ItemsViewModel> Item(ShellContext ctx, string input = "ITEM")
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        var s = (ItemsViewModel)ItemsViewModel.Create(cmd, ctx)!;
        await s.LoadAsync(CancellationToken.None);
        return s;
    }

    static void Fill(ItemsViewModel s, string name)
    {
        s.FormName = name;
        s.FormHsnSac = "7214";
        s.FormUnit = "kg";
        s.FormRate = s.Rates.Single(r => r.BasisPoints == 1800);
        s.FormCessRate = "";
        s.FormDefaultPrice = "1,250.50";
    }

    [Fact]
    public async Task Item_saves_and_appears_in_the_cache()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Item(shell.Context);
        await s.RunAsync("record.new");
        Fill(s, "Steel rod");
        await s.SaveAsync();
        Assert.Null(s.Message);
        await t.Eventually(() => shell.Context.Lookups.Items.Any(i => i.Name == "Steel rod"));
        var item = shell.Context.Lookups.Items.Single();
        Assert.Equal((1800, 0, 125050L, "kg"), (item.RateBp, item.CessBp, item.DefaultPricePaise, item.Unit));
        await t.Eventually(() => s.Items.Any(i => i.Name == "Steel rod"));
    }

    [Fact]
    public async Task Item_rate_choices_are_the_valid_slabs()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Item(shell.Context);
        Assert.Equal([0, 10, 25, 100, 150, 300, 500, 600, 1200, 1800, 2800, 4000], s.Rates.Select(r => r.BasisPoints));
        Assert.Equal("18%", s.Rates.Single(r => r.BasisPoints == 1800).Label);
        Assert.Equal("0.25%", s.Rates.Single(r => r.BasisPoints == 25).Label);
    }

    [Fact]
    public async Task Bad_numbers_say_how_to_write_them()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Item(shell.Context);
        Fill(s, "Rod");
        s.FormDefaultPrice = "12.345";
        await s.SaveAsync();
        Assert.Equal("Enter an amount like 12,345.60.", s.Message);
        s.FormDefaultPrice = "";
        s.FormCessRate = "1.234";
        await s.SaveAsync();
        Assert.Equal("Enter a rate like 18 or 0.25.", s.Message);
    }

    [Fact]
    public async Task Item_conflict_shows_the_current_item()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Item(shell.Context);
        Fill(s, "Steel rod");
        await s.SaveAsync();
        await t.Eventually(() => s.Items.Count == 1);
        s.Selected = s.Items.Single();
        var stored = shell.Context.Lookups.Items.Single();
        await t.OtherOwnerClient().Masters.SaveItemAsync(new SaveItemRequest
        {
            Company = shell.Context.Company!.Id,
            Item = new ItemMsg(stored) { Name = "Steel rod 12mm" },
        }, t.Headers());
        s.FormUnit = "pc";
        await s.SaveAsync();
        Assert.Equal("Someone else changed this item. Showing the current version.", s.Message);
        Assert.Equal("Steel rod 12mm", s.FormName);
        Assert.Equal("kg", s.FormUnit);
    }

    [Fact]
    public async Task Item_with_a_name_selects_it()
    {
        var shell = await t.LoggedInShellAsync([], company: "A Traders");
        var s = await Item(shell.Context);
        Fill(s, "Steel rod");
        await s.SaveAsync();
        await t.Eventually(() => shell.Context.Lookups.Items.Count == 1);
        var again = await Item(shell.Context, "ITEM steel");
        Assert.Equal("Steel rod", again.Selected!.Name);
    }
}
