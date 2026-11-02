namespace OpenHisabKitab.Core.Tests.Accounts;

public class DefaultChartTests
{
    readonly ChartOfAccounts c = DefaultChart.Create();

    [Theory]
    [InlineData(1, "Equity", GroupKind.Equity)]
    [InlineData(4, "Non Current Liabilities", GroupKind.NonCurrentLiabilities)]
    [InlineData(5, "Long Term Borrowings", GroupKind.LongTermBorrowings)]
    [InlineData(6, "Current Liabilities", GroupKind.CurrentLiabilities)]
    [InlineData(7, "Short Term Borrowings", GroupKind.ShortTermBorrowings)]
    [InlineData(13, "Non Current Assets", GroupKind.NonCurrentAssets)]
    [InlineData(14, "Fixed Assets", GroupKind.FixedAssets)]
    [InlineData(15, "Investments", GroupKind.Investments)]
    [InlineData(16, "Current Assets", GroupKind.CurrentAssets)]
    [InlineData(24, "Income", GroupKind.Income)]
    [InlineData(27, "Expenses", GroupKind.Expenses)]
    [InlineData(32, "Depreciation", GroupKind.Depreciation)]
    public void Default_chart_marks_every_heading_kind(int id, string name, GroupKind kind)
    {
        var g = c.Groups.Single(x => x.Kind == kind);
        Assert.Equal(id, g.Id.Value);
        Assert.Equal(name, g.Name);
    }

    [Fact]
    public void Level_one_of_a_nested_group_is_its_heading_child()
    {
        var (chart, nested) = c.AddGroup("Cash Credit", c.Group(GroupKind.BankOverdraft));
        Assert.Equal(GroupKind.ShortTermBorrowings, chart.LevelOneOf(nested)!.Kind);
        Assert.Equal(GroupKind.CurrentLiabilities, chart.RootOf(nested).Kind);
        Assert.Equal(GroupKind.ShortTermBorrowings, chart.LevelOneOf(c.Group(GroupKind.ShortTermBorrowings))!.Kind);
        Assert.Null(chart.LevelOneOf(c.Group(GroupKind.CurrentLiabilities)));
    }
}
