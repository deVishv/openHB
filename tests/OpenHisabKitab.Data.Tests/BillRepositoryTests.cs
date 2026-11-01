using OpenHisabKitab.Data.CompanyDb;

namespace OpenHisabKitab.Data.Tests;

public sealed class BillRepositoryTests : IDisposable
{
    readonly TempDb db = new("company-1.db");
    readonly SqliteConnection c;
    readonly ChartOfAccounts chart;
    readonly LedgerId cash, sales, party;

    public BillRepositoryTests()
    {
        c = db.Open();
        Migrator.Migrate(c, Schema.Company);
        var start = DefaultChart.Create();
        var profile = new CompanyProfile("A Traders", new StateCode("27"), GstRegistration.Unregistered, null, "", 2026);
        Write(tx => CompanyRepository.Create(tx, profile, start));
        (chart, party) = start.AddLedger("Ravi Stores", start.Group(GroupKind.TradeReceivables));
        Write(tx => ChartRepository.SaveLedger(tx, chart.Ledger(party)));
        Write(tx => PartyRepository.Upsert(tx, new Party(party, null, new StateCode("27"), 30, "", 0)));
        cash = chart.Required(RequiredLedger.Cash);
        sales = chart.Required(RequiredLedger.Sales);
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

    long Sale(long number, string total, string date = "2026-06-01", string? partyDoc = null)
    {
        var invoice = new InvoiceHeader(party, TradeDirection.Outward, InvoiceKind.Regular, new StateCode("27"), false,
            partyDoc, null, null, null, Money.Zero, []);
        return Write(tx => VoucherRepository.Insert(tx, new VoucherRow(0, VoucherType.Sale, "Main", 2026, number, $"INV/26-27/{number:D5}",
            DateOnly.Parse(date), "", false, 1, 0, [new PostingLine(party, Money.Parse(total)), new PostingLine(sales, -Money.Parse(total))], invoice)));
    }

    long Receipt(long number, string amount, params AllocationRow[] allocations) =>
        Write(tx => VoucherRepository.Insert(tx, new VoucherRow(0, VoucherType.Receipt, "Main", 2026, number, $"RCT/26-27/{number:D5}",
            new DateOnly(2026, 6, 10), "", false, 1, 0,
            [new PostingLine(cash, Money.Parse(amount)), new PostingLine(party, -Money.Parse(amount))], null, allocations)));

    static OpeningBillRow Opening(long id, string number, string amount, string date = "2026-03-15", string? due = null) =>
        new(id, default, number, DateOnly.Parse(date), due is null ? null : DateOnly.Parse(due), Money.Parse(amount));

    [Fact]
    public void Bills_lists_open_amounts_and_skips_cancelled_vouchers()
    {
        Write(tx => BillRepository.ReplaceOpeningBills(tx, party, [Opening(0, "OB-1", "2000.00", due: "2026-04-15")]));
        var sale = Sale(1, "11800.00");
        var cancelled = Sale(2, "500.00");
        Write(tx => VoucherRepository.Cancel(tx, cancelled, 1));
        Receipt(1, "5000.00", new AllocationRow(party, new BillRef(BillSource.Voucher, sale), Money.Parse("5000.00")));

        var bills = BillRepository.Bills(c, party, PartySide.Receivable, 30, null);
        Assert.Equal(2, bills.Count);
        var opening = bills.Single(b => b.Ref.Source == BillSource.Opening);
        Assert.Equal(("OB-1", Money.Parse("2000.00"), new DateOnly(2026, 4, 15)), (opening.Number, opening.Open, opening.DueDate!.Value));
        var s = bills.Single(b => b.Ref.Source == BillSource.Voucher);
        Assert.Equal(new BillRef(BillSource.Voucher, sale), s.Ref);
        Assert.Equal(("INV/26-27/00001", Money.Parse("11800.00"), Money.Parse("5000.00"), Money.Parse("6800.00")),
            (s.Number, s.Amount, s.Settled, s.Open));
        Assert.Equal(new DateOnly(2026, 7, 1), s.DueDate);
    }

    [Fact]
    public void Bills_carry_the_party_document_number()
    {
        Sale(1, "100.00", partyDoc: "SUP-77");
        Assert.Equal("SUP-77", BillRepository.Bills(c, party, PartySide.Receivable, 0, null).Single().PartyDocNumber);
    }

    [Fact]
    public void Bills_skip_vouchers_on_the_settle_side()
    {
        Sale(1, "100.00");
        Receipt(1, "50.00");
        Assert.Equal("INV/26-27/00001", BillRepository.Bills(c, party, PartySide.Receivable, 0, null).Single().Number);
    }

    [Fact]
    public void Bills_excluding_a_voucher_leaves_out_its_own_allocations()
    {
        var sale = Sale(1, "1000.00");
        var bill = new BillRef(BillSource.Voucher, sale);
        var r1 = Receipt(1, "300.00", new AllocationRow(party, bill, Money.Parse("300.00")));
        Receipt(2, "200.00", new AllocationRow(party, bill, Money.Parse("200.00")));
        Assert.Equal(Money.Parse("500.00"), BillRepository.Bills(c, party, PartySide.Receivable, 0, null).Single().Open);
        Assert.Equal(Money.Parse("800.00"), BillRepository.Bills(c, party, PartySide.Receivable, 0, r1).Single().Open);
    }

    [Fact]
    public void Bills_excluding_a_voucher_does_not_list_it()
    {
        var sale = Sale(1, "1000.00");
        Assert.Empty(BillRepository.Bills(c, party, PartySide.Receivable, 0, sale));
    }

    [Fact]
    public void Replace_opening_bills_keeps_ids_of_kept_rows()
    {
        Assert.Equal(1, BillRepository.OpeningBillsVersion(c, party));
        Write(tx => BillRepository.ReplaceOpeningBills(tx, party, [Opening(0, "OB-1", "100.00"), Opening(0, "OB-2", "200.00")]));
        var first = BillRepository.OpeningBills(c, party);
        Assert.Equal(2, BillRepository.OpeningBillsVersion(c, party));
        var kept = first.Single(b => b.Number == "OB-1");
        Write(tx => BillRepository.ReplaceOpeningBills(tx, party, [kept with { Amount = Money.Parse("150.00") }, Opening(0, "OB-3", "300.00")]));
        var second = BillRepository.OpeningBills(c, party);
        Assert.Equal(["OB-1", "OB-3"], second.Select(b => b.Number));
        Assert.Equal(kept.Id, second[0].Id);
        Assert.Equal(Money.Parse("150.00"), second[0].Amount);
        Assert.Equal(party, second[0].Party);
        Assert.Equal(3, BillRepository.OpeningBillsVersion(c, party));
    }
}
