namespace OpenHisabKitab.Core.Tests.Commands;

public class FunctionCatalogTests
{
    [Fact]
    public void Has_34_unique_four_character_codes()
    {
        Assert.Equal(34, FunctionCatalog.All.Count);
        Assert.Equal(34, FunctionCatalog.All.Select(f => f.Code).Distinct().Count());
        Assert.All(FunctionCatalog.All, f => Assert.True(CommandParser.TryParse(f.Code, out _)));
    }

    [Fact]
    public void Contains_every_spec_code_in_category()
    {
        string Codes(FunctionCategory c) => string.Join(" ", FunctionCatalog.All.Where(f => f.Category == c).Select(f => f.Code));
        Assert.Equal("HELP SETG KEYS CMPY USER AUDT LOCK BKUP DATE FIND RFSH PRNT", Codes(FunctionCategory.System));
        Assert.Equal("ACCT PRTY ITEM", Codes(FunctionCategory.Masters));
        Assert.Equal("SALE PURC CRNT DBNT RCPT PYMT JRNL CNTR", Codes(FunctionCategory.Vouchers));
        Assert.Equal("DAYB LEDG TRLB PNLS BSHT CFLO OUTS", Codes(FunctionCategory.Reports));
        Assert.Equal("GST1 GS3B G2BR EINV", Codes(FunctionCategory.Gst));
    }

    [Fact]
    public void Search_code_prefix_first_then_name_words()
    {
        var r = FunctionCatalog.Search("tr").Select(f => f.Code).ToList();
        Assert.Equal("TRLB", r[0]);
        Assert.Contains("AUDT", r);
        Assert.Equal(r.Count, r.Distinct().Count());
        Assert.Equal(new[] { "TRLB" }, FunctionCatalog.Search("trial").Select(f => f.Code));
    }

    [Fact]
    public void Search_empty_returns_all() => Assert.Equal(FunctionCatalog.All, FunctionCatalog.Search(""));

    [Fact]
    public void Find_ignores_case()
    {
        Assert.Equal("Trial balance", FunctionCatalog.Find("trlb")!.Name);
        Assert.Null(FunctionCatalog.Find("ZZZZ"));
    }
}
