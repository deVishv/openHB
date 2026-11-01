using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;

namespace OpenHisabKitab.Core.Tests.Bills;

public sealed class BillAllocatorTests
{
    static OpenBill B(long id, string date, long open, long settled = 0) =>
        new(new BillRef(BillSource.Voucher, id), new LedgerId(7), $"INV/{id}", null, DateOnly.Parse(date), null,
            new Money(open + settled), new Money(settled));

    [Fact]
    public void Suggest_takes_oldest_first_and_stops_at_the_amount()
    {
        var bills = new[] { B(2, "2026-05-10", 4000_00), B(1, "2026-04-20", 3000_00), B(3, "2026-06-01", 5000_00) };
        var s = BillAllocator.Suggest(bills, new Money(5000_00));
        Assert.Equal([(1L, 3000_00L), (2L, 2000_00L)], s.Select(a => (a.Bill.Id, a.Amount.Paise)));
    }

    [Fact]
    public void Suggest_orders_bills_of_one_date_by_id()
    {
        var bills = new[] { B(9, "2026-04-20", 1000_00), B(4, "2026-04-20", 1000_00) };
        var s = BillAllocator.Suggest(bills, new Money(1500_00));
        Assert.Equal([(4L, 1000_00L), (9L, 500_00L)], s.Select(a => (a.Bill.Id, a.Amount.Paise)));
    }

    [Fact]
    public void Suggest_puts_the_named_bill_first_and_skips_settled_bills()
    {
        var bills = new[] { B(1, "2026-04-20", 0, settled: 3000_00), B(2, "2026-05-10", 4000_00), B(3, "2026-06-01", 5000_00) };
        var s = BillAllocator.Suggest(bills, new Money(6000_00), first: new BillRef(BillSource.Voucher, 3));
        Assert.Equal([(3L, 5000_00L), (2L, 1000_00L)], s.Select(a => (a.Bill.Id, a.Amount.Paise)));
    }

    [Fact]
    public void Suggest_leaves_the_rest_on_account()
    {
        var s = BillAllocator.Suggest([B(1, "2026-04-20", 1000_00)], new Money(2500_00));
        Assert.Equal(1000_00, Money.Sum(s.Select(a => a.Amount)).Paise);
    }

    [Fact]
    public void Suggest_for_nothing_allocates_nothing()
    {
        Assert.Empty(BillAllocator.Suggest([B(1, "2026-04-20", 1000_00)], Money.Zero));
        Assert.Empty(BillAllocator.Suggest([B(1, "2026-04-20", 1000_00)], new Money(-5_00)));
    }

    [Fact]
    public void Suggest_is_always_a_valid_allocation()
    {
        for (var seed = 1; seed <= 500; seed++)
        {
            var rng = new Random(seed * 31337);
            var bills = Enumerable.Range(1, rng.Next(0, 9))
                .Select(i => B(i, new DateOnly(2026, 4, 1).AddDays(rng.Next(0, 300)).ToString("yyyy-MM-dd"),
                    rng.Next(0, 3) == 0 ? 0 : rng.Next(1, 5_000_00), settled: rng.Next(0, 2) == 0 ? 0 : rng.Next(1, 1_000_00)))
                .ToList();
            var amount = new Money(rng.Next(0, 20_000_00));
            var s = BillAllocator.Suggest(bills, amount);

            var totalOpen = Money.Sum(bills.Select(b => b.Open));
            var expected = amount < totalOpen ? amount : totalOpen;
            Assert.Equal(expected, Money.Sum(s.Select(a => a.Amount)));
            var byRef = bills.ToDictionary(b => b.Ref);
            Assert.All(s, a => Assert.True(a.Amount > Money.Zero && a.Amount <= byRef[a.Bill].Open, $"seed {seed}"));
            Assert.Null(AllocationCheck.Check(new LedgerId(7), amount, s, byRef));
        }
    }
}
