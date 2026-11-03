using OpenHisabKitab.Client.Model.Screens.Reports;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Tests.Reports;

public sealed class ReportArgsTests
{
    static readonly DateOnly Today = new(2026, 6, 1);

    [Fact]
    public void Name_and_period_take_trailing_period_words()
    {
        var (name, period, error) = ReportArgs.NameAndPeriod(["Rent", "apr"], Today);
        Assert.Equal(("Rent", new DateRange(new(2026, 4, 1), new(2026, 4, 30)), (string?)null), (name, period, error));

        (name, period, error) = ReportArgs.NameAndPeriod(["Sales", "apr", "01-04-2026", "31-03-2027"], Today);
        Assert.Equal(("Sales apr", new DateRange(new(2026, 4, 1), new(2027, 3, 31)), (string?)null), (name, period, error));

        (name, period, _) = ReportArgs.NameAndPeriod(["HDFC", "Bank"], Today);
        Assert.Equal(("HDFC Bank", (DateRange?)null), (name, period));

        (name, period, _) = ReportArgs.NameAndPeriod(["q1"], Today);
        Assert.Equal(("", new DateRange(new(2026, 4, 1), new(2026, 6, 30))), (name, period));
    }

    [Fact]
    public void Period_across_years_says_one_line()
    {
        var (_, period, error) = ReportArgs.NameAndPeriod(["01-03-2026", "30-04-2026"], Today);
        Assert.Null(period);
        Assert.Equal("Choose a period within one financial year.", error);
        (_, _, error) = ReportArgs.NameAndPeriod(["30-4", "1-4"], Today);
        Assert.Equal("The period ends before it starts.", error);
    }

    [Fact]
    public void Rest_and_date_take_a_trailing_date()
    {
        Assert.Equal(("payable", new DateOnly(2026, 7, 31), (string?)null), ReportArgs.RestAndDate(["payable", "31-07-2026"], Today));
        Assert.Equal(("Ravi Stores", (DateOnly?)null, (string?)null), ReportArgs.RestAndDate(["Ravi", "Stores"], Today));
    }

    [Fact]
    public void Command_carries_explicit_dates() =>
        Assert.Equal("LEDG Sales apr 01-04-2026 31-03-2027",
            ReportArgs.Command("LEDG", "Sales apr", new DateRange(new(2026, 4, 1), new(2027, 3, 31))));
}
