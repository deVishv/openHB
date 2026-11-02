using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class VoucherRepositoryTests : IDisposable
{
    static readonly FinancialYear Fy = new(2026);
    readonly TempDb db = new("company-1.db");
    readonly SqliteConnection c;
    readonly ChartOfAccounts chart = DefaultChart.Create();
    readonly LedgerId cash, sales, capital;

    public VoucherRepositoryTests()
    {
        c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        var profile = new CompanyProfile("A Traders", new StateCode("27"), GstRegistration.Unregistered, null, "", 2026);
        Write(tx => CompanyRepository.Create(tx, profile, chart));
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
        capital = chart.Required(RequiredLedger.Capital);
    }

    public void Dispose()
    {
        c.Dispose();
        db.Dispose();
    }

    T Write<T>(Func<SqliteTransaction, T> work)
    {
        using var tx = SqliteDatabase.BeginWrite(c);
        var result = work(tx);
        tx.Commit();
        return result;
    }

    void Write(Action<SqliteTransaction> work) => Write(tx => { work(tx); return 0; });

    VoucherRow Receipt(long number, DateOnly date, long rupees, int createdBy = 1) => new(
        0, VoucherType.Receipt, "Main", Fy.StartYear, number, $"RCT/26-27/{number:D5}", date, "cash sale", false, createdBy, 0,
        [new PostingLine(cash, Money.FromRupees(rupees)), new PostingLine(sales, Money.FromRupees(-rupees))], null);

    [Fact]
    public void Heads_reads_number_type_and_narration_in_one_query()
    {
        var a = Write(tx => VoucherRepository.Insert(tx, Receipt(1, new(2026, 5, 1), 100)));
        var b = Write(tx => VoucherRepository.Insert(tx, Receipt(2, new(2026, 5, 2), 200) with { Narration = "second" }));
        var heads = VoucherRepository.Heads(c, [a, b, 999]);
        Assert.Equal(2, heads.Count);
        Assert.Equal(new VoucherHead(a, VoucherType.Receipt, "RCT/26-27/00001", "cash sale"), heads[a]);
        Assert.Equal("second", heads[b].Narration);
        Assert.Empty(VoucherRepository.Heads(c, []));
    }

    [Fact]
    public void Take_counts_up_from_one()
    {
        var first = Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Receipt, Fy));
        var second = Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Receipt, Fy));
        Assert.Equal(new NumberSeries("RCT/{FY}/", 5), first.Series);
        Assert.Equal(1, first.Next);
        Assert.Equal(2, second.Next);
        Assert.Equal(1, Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Sale, Fy)).Next);
    }

    [Theory]
    [InlineData(VoucherType.Sale, "INV/{FY}/")]
    [InlineData(VoucherType.Purchase, "PUR/{FY}/")]
    [InlineData(VoucherType.CreditNote, "CN/{FY}/")]
    [InlineData(VoucherType.DebitNote, "DN/{FY}/")]
    [InlineData(VoucherType.Receipt, "RCT/{FY}/")]
    [InlineData(VoucherType.Payment, "PAY/{FY}/")]
    [InlineData(VoucherType.Journal, "JV/{FY}/")]
    [InlineData(VoucherType.Contra, "CON/{FY}/")]
    public void Default_series(VoucherType type, string pattern) =>
        Assert.Equal(new NumberSeries(pattern, 5), Write(tx => NumberSeriesRepository.Take(tx, type, Fy)).Series);

    [Fact]
    public void New_year_starts_at_one()
    {
        Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Receipt, Fy));
        Write(tx => CompanyRepository.AddYear(tx, new FinancialYear(2027)));
        Assert.Equal(1, Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Receipt, new FinancialYear(2027))).Next);
    }

    [Fact]
    public void Take_for_missing_year_throws() =>
        Assert.Throws<InvalidOperationException>(() => Write(tx => NumberSeriesRepository.Take(tx, VoucherType.Receipt, new FinancialYear(2030))));

    [Fact]
    public void Insert_then_get_round_trips_postings()
    {
        var v = Receipt(1, new DateOnly(2026, 5, 10), 500);
        var id = Write(tx => VoucherRepository.Insert(tx, v));
        Assert.Equivalent(v with { Id = id, RowVersion = 1 }, VoucherRepository.Get(c, id), strict: true);
        Assert.Equal(
            [new PostingRecord(id, v.Date, cash, Money.FromRupees(500)), new PostingRecord(id, v.Date, sales, Money.FromRupees(-500))],
            VoucherRepository.PostingsOf(c, id));
    }

    [Fact]
    public void Invoice_round_trips()
    {
        var (c1, party) = chart.AddLedger("Ravi Stores", chart.Group(GroupKind.TradeReceivables));
        Write(tx => ChartRepository.SaveLedger(tx, c1.Ledger(party)));
        var line = new InvoiceLineRow(null, sales, "Steel rod", "7214", new Quantity(2500), Money.Parse("400.00"), Money.Zero,
            new GstRate(1800), new GstRate(0), false, Money.Parse("1000.00"), Money.Parse("90.00"), Money.Parse("90.00"),
            Money.Zero, Money.Zero, Money.Zero);
        var invoice = new InvoiceHeader(party, TradeDirection.Outward, InvoiceKind.Regular, new StateCode("27"), false,
            null, null, null, null, Money.Zero, [line]);
        var v = new VoucherRow(0, VoucherType.Sale, "Main", 2026, 1, "INV/26-27/00001", new DateOnly(2026, 6, 1), "", false, 1, 0,
            [new PostingLine(party, Money.Parse("1180.00")), new PostingLine(sales, Money.Parse("-1000.00")),
             new PostingLine(chart.Required(RequiredLedger.OutputCgst), Money.Parse("-90.00")),
             new PostingLine(chart.Required(RequiredLedger.OutputSgst), Money.Parse("-90.00"))],
            invoice);
        var id = Write(tx => VoucherRepository.Insert(tx, v));
        Assert.Equivalent(v with { Id = id, RowVersion = 1 }, VoucherRepository.Get(c, id), strict: true);
    }

    [Fact]
    public void Update_with_stale_row_version_changes_nothing()
    {
        var id = Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 500)));
        var current = VoucherRepository.Get(c, id)!;
        var edited = current with { Date = new DateOnly(2026, 5, 11), Postings = Receipt(1, default, 700).Postings };
        Assert.False(Write(tx => VoucherRepository.Update(tx, edited, expectedRowVersion: 7)));
        Assert.Equivalent(current, VoucherRepository.Get(c, id), strict: true);
        Assert.True(Write(tx => VoucherRepository.Update(tx, edited, expectedRowVersion: 1)));
        var after = VoucherRepository.Get(c, id)!;
        Assert.Equal(2, after.RowVersion);
        Assert.Equal(new DateOnly(2026, 5, 11), after.Date);
        Assert.All(VoucherRepository.PostingsOf(c, id), p => Assert.Equal(new DateOnly(2026, 5, 11), p.Date));
        Assert.Equal(Money.FromRupees(700), VoucherRepository.PostingsOf(c, id)[0].Amount);
    }

    [Fact]
    public void Cancel_removes_postings_but_keeps_number()
    {
        var id = Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 500)));
        Assert.False(Write(tx => VoucherRepository.Cancel(tx, id, 9)));
        Assert.True(Write(tx => VoucherRepository.Cancel(tx, id, 1)));
        var v = VoucherRepository.Get(c, id)!;
        Assert.True(v.Cancelled);
        Assert.Equal("RCT/26-27/00001", v.DisplayNumber);
        Assert.Empty(v.Postings);
        Assert.Empty(VoucherRepository.AllPostings(c));
    }

    [Fact]
    public void List_filters_by_range_type_and_creator()
    {
        Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 100, createdBy: 1)));
        Write(tx => VoucherRepository.Insert(tx, Receipt(2, new DateOnly(2026, 5, 12), 200, createdBy: 2)));
        Write(tx => VoucherRepository.Insert(tx, Receipt(3, new DateOnly(2026, 7, 1), 300, createdBy: 2)));
        var may = new DateRange(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));
        Assert.Equal([1L, 2L], VoucherRepository.List(c, may, null, null).Select(v => v.Number));
        Assert.Equal([2L], VoucherRepository.List(c, may, VoucherType.Receipt, 2).Select(v => v.Number));
        Assert.Empty(VoucherRepository.List(c, may, VoucherType.Payment, null));
        Assert.Equal(2, VoucherRepository.List(c, may, null, 2).Single().Postings.Count);
    }

    [Fact]
    public void Posting_to_unknown_ledger_throws()
    {
        var bad = Receipt(1, new DateOnly(2026, 5, 10), 500) with
        {
            Postings = [new PostingLine(new LedgerId(9999), Money.FromRupees(1)), new PostingLine(cash, Money.FromRupees(-1))],
        };
        Assert.Throws<SqliteException>(() => Write(tx => VoucherRepository.Insert(tx, bad)));
    }

    [Fact]
    public void Same_number_twice_throws()
    {
        Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 500)));
        Assert.Throws<SqliteException>(() => Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 11), 50))));
    }

    [Fact]
    public void Has_postings()
    {
        Assert.False(ChartRepository.HasPostings(c, cash));
        Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 500)));
        Assert.True(ChartRepository.HasPostings(c, cash));
        Assert.False(ChartRepository.HasPostings(c, capital));
    }

    [Fact]
    public void Lock_round_trips()
    {
        Assert.Null(LockRepository.LockedUpTo(c));
        Write(tx => LockRepository.Set(tx, new DateOnly(2026, 6, 30)));
        Assert.Equal(new DateOnly(2026, 6, 30), LockRepository.LockedUpTo(c));
        Write(tx => LockRepository.Set(tx, null));
        Assert.Null(LockRepository.LockedUpTo(c));
    }
    (LedgerId Party, VoucherRow Sale) Sale(long number, string total = "1180.00")
    {
        var (c1, party) = chart.AddLedger($"Party {number}", chart.Group(GroupKind.TradeReceivables));
        Write(tx => ChartRepository.SaveLedger(tx, c1.Ledger(party)));
        Write(tx => PartyRepository.Upsert(tx, new Party(party, null, new StateCode("27"), 30, "", 0)));
        var invoice = new InvoiceHeader(party, TradeDirection.Outward, InvoiceKind.ExportUnderLut, new StateCode("96"), false,
            null, null, null, null, Money.Zero, [], new ExportDetails("INNSA1", "1234567", new DateOnly(2026, 6, 2)));
        return (party, new VoucherRow(0, VoucherType.Sale, "Main", 2026, number, $"INV/26-27/{number:D5}", new DateOnly(2026, 6, 1), "",
            false, 1, 0, [new PostingLine(party, Money.Parse(total)), new PostingLine(sales, -Money.Parse(total))], invoice));
    }

    [Fact]
    public void Allocations_and_export_details_round_trip()
    {
        var (party, sale) = Sale(1);
        var saleId = Write(tx => VoucherRepository.Insert(tx, sale));
        var receipt = Receipt(1, new DateOnly(2026, 6, 5), 500) with
        {
            Postings = [new PostingLine(cash, Money.FromRupees(500)), new PostingLine(party, Money.FromRupees(-500))],
            Allocations = [new AllocationRow(party, new BillRef(BillSource.Voucher, saleId), Money.FromRupees(500))],
            RequestId = "abc",
        };
        var id = Write(tx => VoucherRepository.Insert(tx, receipt));
        Assert.Equivalent(receipt with { Id = id, RowVersion = 1 }, VoucherRepository.Get(c, id), strict: true);
        Assert.Equivalent(sale with { Id = saleId, RowVersion = 1 }, VoucherRepository.Get(c, saleId), strict: true);
        Assert.Equal(id, BillRepository.VoucherByRequestId(c, "abc"));
        Assert.Null(BillRepository.VoucherByRequestId(c, "other"));
    }

    [Fact]
    public void Cancel_removes_the_vouchers_own_allocations()
    {
        var (party, sale) = Sale(1);
        var saleId = Write(tx => VoucherRepository.Insert(tx, sale));
        var bill = new BillRef(BillSource.Voucher, saleId);
        var receipt = Receipt(1, new DateOnly(2026, 6, 5), 500) with
        {
            Postings = [new PostingLine(cash, Money.FromRupees(500)), new PostingLine(party, Money.FromRupees(-500))],
            Allocations = [new AllocationRow(party, bill, Money.FromRupees(500))],
        };
        var id = Write(tx => VoucherRepository.Insert(tx, receipt));
        Assert.Equal(Money.FromRupees(500), BillRepository.SettledAgainst(c, bill));
        Assert.True(Write(tx => VoucherRepository.Cancel(tx, id, 1)));
        Assert.Empty(VoucherRepository.Get(c, id)!.Allocations);
        Assert.Equal(Money.Zero, BillRepository.SettledAgainst(c, bill));
    }

    [Fact]
    public void Request_id_is_unique()
    {
        Write(tx => VoucherRepository.Insert(tx, Receipt(1, new DateOnly(2026, 5, 10), 500) with { RequestId = "same" }));
        var e = Assert.Throws<SqliteException>(() =>
            Write(tx => VoucherRepository.Insert(tx, Receipt(2, new DateOnly(2026, 5, 10), 500) with { RequestId = "same" })));
        Assert.Equal(19, e.SqliteErrorCode);
    }
}
