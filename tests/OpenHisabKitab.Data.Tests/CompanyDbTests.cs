using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class CompanyDbTests : IDisposable
{
    static readonly GstData Gst = GstData.LoadEmbedded();
    readonly TempDb db = new("company-1.db");
    readonly SqliteConnection c;
    readonly ChartOfAccounts chart = DefaultChart.Create();

    public CompanyDbTests()
    {
        c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        Write(tx => CompanyRepository.Create(tx, Profile(), chart));
    }

    public void Dispose()
    {
        c.Dispose();
        db.Dispose();
    }

    static Gstin G(string s) => Gstin.TryParse(s, Gst, out var g) ? g : throw new ArgumentException(s);

    static CompanyProfile Profile(Gstin? gstin = null) =>
        new("A Traders", new StateCode("27"), GstRegistration.Regular, gstin ?? G("27AAPFU0939F1ZV"), "1 MG Road, Pune", 2026);

    T Write<T>(Func<SqliteTransaction, T> work)
    {
        using var tx = SqliteDatabase.BeginWrite(c);
        var result = work(tx);
        tx.Commit();
        return result;
    }

    void Write(Action<SqliteTransaction> work) => Write(tx => { work(tx); return 0; });

    [Fact]
    public void Chart_round_trips()
    {
        var loaded = ChartRepository.Load(c);
        Assert.Equal(chart.Groups, loaded.Groups);
        Assert.Equal(chart.Ledgers, loaded.Ledgers);
    }

    [Fact]
    public void Added_and_renamed_ledger_round_trips()
    {
        var (c1, id) = chart.AddLedger("HDFC Bank", chart.Group(GroupKind.BankAccounts));
        var c2 = c1.RenameLedger(id, "HDFC Bank 0042");
        Write(tx => ChartRepository.SaveLedger(tx, c2.Ledger(id)));
        Assert.Equal("HDFC Bank 0042", ChartRepository.Load(c).Ledger(id).Name);
    }

    [Fact]
    public void Added_group_round_trips()
    {
        var (c1, id) = chart.AddGroup("Branch Cash", chart.Group(GroupKind.CashInHand));
        Write(tx => ChartRepository.SaveGroup(tx, c1.Groups.Single(g => g.Id == id)));
        Assert.Equal(c1.Groups, ChartRepository.Load(c).Groups);
    }

    [Fact]
    public void Deleted_ledger_is_gone()
    {
        var (c1, id) = chart.AddLedger("Temp", chart.Group(GroupKind.CashInHand));
        Write(tx => ChartRepository.SaveLedger(tx, c1.Ledger(id)));
        Write(tx => ChartRepository.DeleteLedger(tx, id));
        Assert.False(ChartRepository.Load(c).Contains(id));
    }

    [Fact]
    public void Profile_round_trips()
    {
        Assert.Equal(Profile(), CompanyRepository.GetProfile(c));
        var unregistered = Profile() with { Registration = GstRegistration.Unregistered, Gstin = null };
        Write(tx => CompanyRepository.UpdateProfile(tx, unregistered));
        Assert.Equal(unregistered, CompanyRepository.GetProfile(c));
    }

    [Fact]
    public void Opening_round_trips()
    {
        var cash = chart.Required(RequiredLedger.Cash);
        Write(tx => ChartRepository.SetOpening(tx, cash, Money.FromRupees(5000)));
        Assert.Equal(Money.FromRupees(5000), ChartRepository.Openings(c)[cash]);
        Assert.False(ChartRepository.Openings(c).ContainsKey(chart.Required(RequiredLedger.Capital)));
    }

    [Fact]
    public void Years_start_with_first_and_grow()
    {
        Assert.Equal([new FinancialYear(2026)], CompanyRepository.Years(c));
        Write(tx => CompanyRepository.AddYear(tx, new FinancialYear(2027)));
        Assert.Equal([new FinancialYear(2026), new FinancialYear(2027)], CompanyRepository.Years(c));
    }

    [Fact]
    public void Setting_round_trips()
    {
        Assert.Null(CompanyRepository.GetSetting(c, "invoice.roundOff"));
        Write(tx => CompanyRepository.SetSetting(tx, "invoice.roundOff", "false"));
        Assert.Equal("false", CompanyRepository.GetSetting(c, "invoice.roundOff"));
    }

    [Fact]
    public void Party_round_trips_and_bumps_row_version()
    {
        var (c1, id) = chart.AddLedger("Ravi Stores", chart.Group(GroupKind.TradeReceivables));
        Write(tx => ChartRepository.SaveLedger(tx, c1.Ledger(id)));
        var p = new Party(id, G("29AAGCB7383J1Z4"), new StateCode("29"), 30, "Bengaluru", 0);
        Write(tx => PartyRepository.Upsert(tx, p));
        Assert.Equal(p with { RowVersion = 1 }, PartyRepository.Get(c, id));
        Write(tx => PartyRepository.Upsert(tx, p with { CreditDays = 45 }));
        Assert.Equal(p with { CreditDays = 45, RowVersion = 2 }, PartyRepository.Get(c, id));
        Assert.Single(PartyRepository.List(c));
    }

    [Fact]
    public void Party_without_gstin_round_trips()
    {
        var (c1, id) = chart.AddLedger("Walk In", chart.Group(GroupKind.TradeReceivables));
        Write(tx => ChartRepository.SaveLedger(tx, c1.Ledger(id)));
        var p = new Party(id, null, new StateCode("27"), 0, "", 0);
        Write(tx => PartyRepository.Upsert(tx, p));
        Assert.Null(PartyRepository.Get(c, id)!.Gstin);
    }

    [Fact]
    public void Party_for_unknown_ledger_throws() =>
        Assert.Throws<SqliteException>(() =>
            Write(tx => PartyRepository.Upsert(tx, new Party(new LedgerId(9999), null, new StateCode("27"), 0, "", 0))));

    [Fact]
    public void Item_round_trips()
    {
        var item = new Item(0, "Steel Rod", "7214", false, "KG", new GstRate(1800), new GstRate(0), Money.Parse("65.50"), 0);
        var id = Write(tx => ItemRepository.Insert(tx, item));
        var stored = ItemRepository.Get(c, id)!;
        Assert.Equal(item with { Id = id, RowVersion = 1 }, stored);
        Assert.Equal(1800, stored.Rate.BasisPoints);
        Write(tx => ItemRepository.Update(tx, stored with { DefaultPrice = Money.Parse("70.00") }));
        Assert.Equal(2, ItemRepository.Get(c, id)!.RowVersion);
        Assert.Single(ItemRepository.List(c));
    }

    [Fact]
    public void Item_name_is_unique_ignoring_case()
    {
        var item = new Item(0, "Steel Rod", "7214", false, "KG", new GstRate(1800), new GstRate(0), Money.Zero, 0);
        Write(tx => ItemRepository.Insert(tx, item));
        Assert.Throws<SqliteException>(() => Write(tx => ItemRepository.Insert(tx, item with { Name = "STEEL ROD" })));
    }
}
