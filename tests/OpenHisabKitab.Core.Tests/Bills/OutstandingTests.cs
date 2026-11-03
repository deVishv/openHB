using OpenHisabKitab.Core.Bills;

namespace OpenHisabKitab.Core.Tests.Bills;

/// <summary>Outstanding bills with ageing (spec 6.3, OUTS; Plan 6 Task 4 rules). Figures worked by hand.</summary>
public class OutstandingTests
{
    static Money R(decimal rupees) => new((long)(rupees * 100));

    static readonly LedgerId A = new(101);
    static readonly LedgerId S = new(102);
    static readonly DateOnly AsAt = new(2026, 7, 31);

    static OpenBill Bill(long id, LedgerId party, DateOnly date, DateOnly? due, decimal amount, decimal settled = 0) =>
        new(new BillRef(BillSource.Voucher, id), party, $"B{id}", null, date, due, R(amount), R(settled));

    static IReadOnlyList<Money> Rs(params decimal[] rupees) => [.. rupees.Select(R)];

    [Fact]
    public void Ages_bills_into_buckets_with_on_account()
    {
        var bills = new[]
        {
            Bill(1, A, new(2026, 4, 1), new(2026, 5, 1), 5000, settled: 1000),
            Bill(2, A, new(2026, 7, 15), new(2026, 8, 14), 3000),
            Bill(3, A, new(2026, 5, 20), new(2026, 6, 20), 2500),
        };
        var r = Outstanding.Build(AsAt, PartySide.Receivable, AgeingBuckets.Default, [new PartyPosition(A, R(9000), bills)]);

        var a = Assert.Single(r.Parties);
        Assert.Equal(Rs(3000, 0, 2500, 0, 4000), a.Buckets);
        Assert.Equal(R(-500), a.OnAccount);
        Assert.Equal(R(9000), a.Total);
        Assert.Equal(new[] { 91, -14, 41 }, a.Bills.Select(b => b.DaysOverdue));
        Assert.Equal(new[] { 4, 0, 2 }, a.Bills.Select(b => b.Bucket));
        Assert.Equal(R(4000), a.Bills[0].Bill.Open);
    }

    [Fact]
    public void Payable_side_turns_credit_positive()
    {
        var r = Outstanding.Build(AsAt, PartySide.Payable, AgeingBuckets.Default,
            [new PartyPosition(S, R(-2000), [Bill(9, S, new(2026, 7, 1), new(2026, 7, 31), 2000)])]);
        var s = Assert.Single(r.Parties);
        Assert.Equal(R(2000), s.Total);
        Assert.Equal(Money.Zero, s.OnAccount);
        Assert.Equal(Rs(2000, 0, 0, 0, 0), s.Buckets);
    }

    [Fact]
    public void Opening_bill_without_due_date_ages_from_its_date()
    {
        var opening = new OpenBill(new BillRef(BillSource.Opening, 1), A, "OB/1", null, new(2026, 3, 1), null, R(100), Money.Zero);
        var r = Outstanding.Build(AsAt, PartySide.Receivable, AgeingBuckets.Default, [new PartyPosition(A, R(100), [opening])]);
        var bill = Assert.Single(Assert.Single(r.Parties).Bills);
        Assert.Equal(new DateOnly(2026, 3, 1), bill.Due);
        Assert.Equal(152, bill.DaysOverdue);
        Assert.Equal(4, bill.Bucket);
    }

    [Fact]
    public void Bucket_totals_sum_the_parties()
    {
        var r = Outstanding.Build(AsAt, PartySide.Receivable, AgeingBuckets.Default,
        [
            new PartyPosition(A, R(1500), [Bill(1, A, new(2026, 7, 20), new(2026, 7, 20), 1000), Bill(2, A, new(2026, 6, 1), new(2026, 6, 1), 500)]),
            new PartyPosition(new LedgerId(103), R(700), [Bill(3, new LedgerId(103), new(2026, 7, 25), new(2026, 7, 25), 900)]),
            new PartyPosition(new LedgerId(104), Money.Zero, []),
            new PartyPosition(new LedgerId(105), Money.Zero, [Bill(4, new LedgerId(105), new(2026, 7, 1), null, 300, settled: 300)]),
        ]);
        Assert.Equal(new[] { A, new LedgerId(103) }, r.Parties.Select(p => p.Party));
        Assert.Equal(Rs(0, 1900, 500, 0, 0), r.BucketTotals);
        Assert.Equal(R(-200), r.OnAccountTotal);
        Assert.Equal(R(2200), r.Total);
        Assert.DoesNotContain(r.Parties[0].Bills, b => b.Bill.Open <= Money.Zero);
    }

    [Theory]
    [InlineData("30,60,90", new[] { 30, 60, 90 })]
    [InlineData(" 45 ", new[] { 45 })]
    [InlineData("7, 14, 21, 28, 999", new[] { 7, 14, 21, 28, 999 })]
    public void Ageing_days_parse(string text, int[] days)
    {
        Assert.True(AgeingBuckets.TryParse(text, out var b));
        Assert.Equal(days, b.Days);
        Assert.Equal(days.Length + 2, b.Count);
    }

    [Theory]
    [InlineData("60,30")]
    [InlineData("10,20,30,40,50,60")]
    [InlineData("0,30")]
    [InlineData("30,1000")]
    [InlineData("30,30")]
    [InlineData("")]
    [InlineData("thirty")]
    public void Ageing_days_reject_bad_text(string text) => Assert.False(AgeingBuckets.TryParse(text, out _));

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(90, 3)]
    [InlineData(91, 4)]
    public void Bucket_of_days_overdue(int days, int bucket) => Assert.Equal(bucket, AgeingBuckets.Default.BucketOf(days));
}
