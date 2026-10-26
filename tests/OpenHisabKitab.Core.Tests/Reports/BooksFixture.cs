namespace OpenHisabKitab.Core.Tests.Reports;

/// <summary>
/// Default chart plus Rent. Books start 1-4-2025 with Cash 50,000.00 Dr and Capital 50,000.00 Cr (or 49,990.00 Cr).
/// V1 10-04-2025 Cash Dr / Sales Cr 10,000.00. V2 20-04-2025 Rent Dr / Cash Cr 2,000.00.
/// V3 05-05-2026 Cash Dr / Sales Cr 3,000.00.
/// </summary>
public sealed class BooksFixture
{
    public BooksFixture(long capitalOpeningPaise = -5000000)
    {
        var c = DefaultChart.Create();
        (Chart, Rent) = c.AddLedger("Rent", c.Group(GroupKind.OtherExpenses));
        Cash = Chart.Required(RequiredLedger.Cash);
        Capital = Chart.Required(RequiredLedger.Capital);
        Sales = Chart.Required(RequiredLedger.Sales);
        ProfitAndLoss = Chart.Required(RequiredLedger.ProfitAndLoss);
        Openings = new Dictionary<LedgerId, Money> { [Cash] = new(5000000), [Capital] = new(capitalOpeningPaise) };
        Postings =
        [
            new(1, new(2025, 4, 10), Cash, new(1000000)), new(1, new(2025, 4, 10), Sales, new(-1000000)),
            new(2, new(2025, 4, 20), Rent, new(200000)), new(2, new(2025, 4, 20), Cash, new(-200000)),
            new(3, new(2026, 5, 5), Cash, new(300000)), new(3, new(2026, 5, 5), Sales, new(-300000)),
        ];
        Books = new Books(Chart, BooksStart, Openings, Postings);
    }

    public static readonly DateOnly BooksStart = new(2025, 4, 1);

    public ChartOfAccounts Chart { get; }
    public LedgerId Cash { get; }
    public LedgerId Capital { get; }
    public LedgerId Sales { get; }
    public LedgerId Rent { get; }
    public LedgerId ProfitAndLoss { get; }
    public Dictionary<LedgerId, Money> Openings { get; }
    public List<PostingRecord> Postings { get; }
    public Books Books { get; }
}
