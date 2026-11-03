namespace OpenHisabKitab.Core.Tests.Gst;

public class GstDataTests
{
    readonly GstData d = GstData.LoadEmbedded();

    [Fact]
    public void Loads_states()
    {
        Assert.Equal("Gujarat", d.State(new StateCode("24")).Name);
        Assert.Equal(StateType.UnionTerritory, d.State(new StateCode("04")).Type);
        Assert.Equal(StateType.UnionTerritoryWithLegislature, d.State(new StateCode("07")).Type);
        Assert.Equal(StateType.Other, d.State(new StateCode("96")).Type);
        Assert.Equal("2026.11.1", d.Version);
    }

    [Fact]
    public void Unknown_state_throws() => Assert.Throws<KeyNotFoundException>(() => d.State(new StateCode("25")));

    [Fact]
    public void B2cl_threshold_by_date()
    {
        Assert.Equal(Money.FromRupees(250000), d.B2clThreshold(new DateOnly(2024, 7, 31)));
        Assert.Equal(Money.FromRupees(100000), d.B2clThreshold(new DateOnly(2024, 8, 1)));
    }

    [Theory]
    [InlineData(1800, 2026, 10, 26, true)]
    [InlineData(4000, 2025, 9, 21, false)]
    [InlineData(4000, 2025, 9, 22, true)]
    [InlineData(1700, 2026, 10, 26, false)]
    [InlineData(25, 2026, 10, 26, true)]
    [InlineData(1800, 2017, 6, 30, false)]
    public void Rate_validity(int bp, int y, int m, int day, bool ok) =>
        Assert.Equal(ok, d.IsValidRate(new GstRate(bp), new DateOnly(y, m, day)));

    [Theory]
    [InlineData("2")]
    [InlineData("ab")]
    [InlineData("123")]
    public void StateCode_requires_two_digits(string v) => Assert.Throws<ArgumentException>(() => new StateCode(v));

    [Fact]
    public void GstRate_rejects_negative() => Assert.Throws<ArgumentException>(() => new GstRate(-1));

    [Fact]
    public void Parse_rejects_malformed_json() => Assert.Throws<FormatException>(() => GstData.Parse("{"));

    [Fact]
    public void Parse_rejects_duplicate_states() =>
        Assert.Throws<FormatException>(() => GstData.Parse(Json(
            states: """[{"code":"24","name":"A","type":"State"},{"code":"24","name":"B","type":"State"}]""")));

    [Fact]
    public void Parse_rejects_bad_state_code() =>
        Assert.Throws<FormatException>(() => GstData.Parse(Json(states: """[{"code":"2","name":"A","type":"State"}]""")));

    [Fact]
    public void Parse_rejects_unknown_state_type() =>
        Assert.Throws<FormatException>(() => GstData.Parse(Json(states: """[{"code":"24","name":"A","type":"Planet"}]""")));

    [Fact]
    public void Parse_rejects_overlapping_thresholds() =>
        Assert.Throws<FormatException>(() => GstData.Parse(Json(
            thresholds: """[{"key":"b2clInvoiceValue","paise":1,"from":"2020-01-01","to":"2021-01-01"},{"key":"b2clInvoiceValue","paise":2,"from":"2020-06-01","to":null}]""")));

    [Fact]
    public void Parse_rejects_reversed_dates() =>
        Assert.Throws<FormatException>(() => GstData.Parse(Json(
            rates: """[{"basisPoints":1800,"from":"2020-01-01","to":"2019-01-01"}]""")));

    [Fact]
    public void Threshold_before_any_entry_throws() =>
        Assert.Throws<KeyNotFoundException>(() => d.B2clThreshold(new DateOnly(2017, 6, 30)));

    static string Json(string states = "[]", string rates = "[]", string thresholds = "[]") =>
        "{\"version\":\"t\",\"sources\":[],\"states\":" + states + ",\"rates\":" + rates +
        ",\"thresholds\":" + thresholds + ",\"schemas\":{}}";
}
