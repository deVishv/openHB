using OpenHisabKitab.Client.Model.Screens;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.Model.Tests.Screens;

public sealed class DateTests : IDisposable
{
    readonly ClientTestHost t = new();
    readonly ShellContext ctx;
    readonly List<string> reported = [];

    public DateTests()
    {
        ctx = t.Shell().Context;
        ctx.WorkingDate = new DateOnly(2026, 6, 1);
        ctx.Reported += reported.Add;
    }

    public void Dispose() => t.Dispose();

    IScreen? Date(string input)
    {
        Assert.True(CommandParser.TryParse(input, out var cmd));
        return DateViewModel.Create(cmd, ctx);
    }

    void OpenCompany(params int[] startYears) =>
        ctx.OpenCompany(new CompanyEntry { Id = 1, Name = "A Traders" }, new FinancialYear(startYears[^1]), [.. startYears.Select(y => new FinancialYear(y))]);

    [Fact]
    public void Date_with_valid_argument_sets_the_date_and_opens_no_tab()
    {
        Assert.Null(Date("DATE 15-4"));
        Assert.Equal(new DateOnly(2026, 4, 15), ctx.WorkingDate);
        Assert.Equal("Working date 15-04-2026.", reported.Single());
        Assert.Null(Date("date today"));
        Assert.Equal(new DateOnly(2026, 6, 1), ctx.WorkingDate);
    }

    [Theory]
    [InlineData("DATE")]
    [InlineData("DATE 31-02-2026")]
    [InlineData("DATE soon")]
    public void Date_without_a_good_argument_opens_the_screen(string input)
    {
        var screen = Assert.IsType<DateViewModel>(Date(input));
        Assert.Equal(new DateOnly(2026, 6, 1), ctx.WorkingDate);
        Assert.Equal(input == "DATE" ? null : "Enter a date like 15-04-2026, 15-4, or today.", screen.Message);
        Assert.Equal("01-06-2026", screen.Input);
    }

    [Fact]
    public async Task The_screen_applies_on_save_and_names_a_mistake()
    {
        var screen = (DateViewModel)Date("DATE")!;
        screen.Input = "1-7";
        await screen.SaveAsync();
        Assert.Equal((new DateOnly(2026, 7, 1), "Working date 01-07-2026."), (ctx.WorkingDate, screen.Message));
        screen.Input = "nope";
        await screen.SaveAsync();
        Assert.Equal((new DateOnly(2026, 7, 1), "Enter a date like 15-04-2026, 15-4, or today."), (ctx.WorkingDate, screen.Message));
    }

    [Fact]
    public void Date_outside_every_year_is_refused()
    {
        OpenCompany(2026);
        Assert.IsType<DateViewModel>(Date("DATE 01-01-2030"));
        Assert.Equal(new DateOnly(2026, 6, 1), ctx.WorkingDate);
        Assert.Equal("No financial year holds 01-01-2030. Add one in CMPY.", ((DateViewModel)Date("DATE 01-01-2030")!).Message);
    }

    [Fact]
    public void Date_in_another_year_moves_the_title()
    {
        OpenCompany(2025, 2026);
        var shell = t.Shell();
        var titles = new List<string>();
        ctx.CompanyChanged += () => titles.Add(ctx.Year!.Value.Label);
        Assert.Null(Date("DATE 10-03-2026"));
        Assert.Equal(new FinancialYear(2025), ctx.Year);
        Assert.Equal(["2025-26"], titles);
        shell.GetHashCode();
    }

    [Fact]
    public async Task Dates_show_in_the_chosen_format()
    {
        await ctx.Settings.SetAsync("ui.dateFormat", "\"yyyy-mm-dd\"", Core.Settings.SettingLayer.Machine, null);
        Date("DATE 15-4");
        Assert.Equal("Working date 2026-04-15.", reported.Single());
    }
}
