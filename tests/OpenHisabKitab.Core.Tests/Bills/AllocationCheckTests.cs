using OpenHisabKitab.Core.Accounts;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Bills;

namespace OpenHisabKitab.Core.Tests.Bills;

public sealed class AllocationCheckTests
{
    static readonly LedgerId Party = new(7);

    static OpenBill B(long id, long open, int party = 7) =>
        new(R(id), new LedgerId(party), $"INV/{id}", null, new DateOnly(2026, 4, 20), null, new Money(open), Money.Zero);

    static BillRef R(long id) => new(BillSource.Voucher, id);

    static Dictionary<BillRef, OpenBill> Bills(params OpenBill[] bills) => bills.ToDictionary(b => b.Ref);

    [Theory]
    [InlineData(0L, AllocationError.NotPositive)]
    [InlineData(-1L, AllocationError.NotPositive)]
    [InlineData(4000_01L, AllocationError.MoreThanOpen)]
    public void Check_refuses_bad_amounts(long paise, AllocationError expected)
    {
        var p = AllocationCheck.Check(Party, new Money(10000_00), [new Allocation(R(1), new Money(paise))], Bills(B(1, 4000_00)));
        Assert.Equal(new AllocationProblem(expected, R(1)), p);
    }

    [Fact]
    public void Check_accepts_up_to_the_open_amount()
    {
        Assert.Null(AllocationCheck.Check(Party, new Money(10000_00), [new Allocation(R(1), new Money(4000_00))], Bills(B(1, 4000_00))));
    }

    [Fact]
    public void Check_refuses_more_than_the_settlement()
    {
        var p = AllocationCheck.Check(Party, new Money(5000_00),
            [new Allocation(R(1), new Money(3000_00)), new Allocation(R(2), new Money(3000_00))], Bills(B(1, 3000_00), B(2, 3000_00)));
        Assert.Equal(new AllocationProblem(AllocationError.MoreThanSettlement, null), p);
    }

    [Fact]
    public void Check_refuses_a_bill_of_another_party_or_unknown_or_twice()
    {
        var bills = Bills(B(1, 3000_00), B(2, 3000_00, party: 8));
        Assert.Equal(new AllocationProblem(AllocationError.OtherParty, R(2)),
            AllocationCheck.Check(Party, new Money(10000_00), [new Allocation(R(2), new Money(100))], bills));
        Assert.Equal(new AllocationProblem(AllocationError.UnknownBill, R(3)),
            AllocationCheck.Check(Party, new Money(10000_00), [new Allocation(R(3), new Money(100))], bills));
        Assert.Equal(new AllocationProblem(AllocationError.Duplicate, R(1)),
            AllocationCheck.Check(Party, new Money(10000_00), [new Allocation(R(1), new Money(100)), new Allocation(R(1), new Money(100))], bills));
    }

    [Theory]
    [InlineData(PartySide.Receivable, 100, true)]
    [InlineData(PartySide.Receivable, -100, false)]
    [InlineData(PartySide.Payable, -100, true)]
    [InlineData(PartySide.Payable, 100, false)]
    public void IsBill_follows_the_party_side(PartySide side, long paise, bool expected) =>
        Assert.Equal(expected, PartySides.IsBill(side, new Money(paise)));
}
