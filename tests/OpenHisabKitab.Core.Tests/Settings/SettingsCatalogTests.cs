using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Core.Tests.Settings;

public sealed class SettingsCatalogTests
{
    [Theory]
    [InlineData("ui.fontSize", "29", SettingLayer.User, "Setting ui.fontSize must be a whole number from 12 to 28.")]
    [InlineData("ui.fontFamily", "\"Arial\"", SettingLayer.Machine, "Setting ui.fontFamily must be one of: Segoe UI, Tahoma.")]
    [InlineData("invoice.roundOff", "true", SettingLayer.User, "Setting invoice.roundOff cannot be set for a user.")]
    [InlineData("connection.port", "47250", SettingLayer.User, "Setting connection.port cannot be set for a user.")]
    [InlineData("ui.fontSize", "14", SettingLayer.Company, "Setting ui.fontSize cannot be set for a company.")]
    [InlineData("invoice.roundOff", "true", SettingLayer.Machine, "Setting invoice.roundOff cannot be set for this PC.")]
    [InlineData("ui.fontsize", "14", SettingLayer.User, "No setting named ui.fontsize.")]
    [InlineData("ui.fontSize", "14.5", SettingLayer.User, "Setting ui.fontSize must be a whole number from 12 to 28.")]
    [InlineData("ui.fontSize", "\"14\"", SettingLayer.User, "Setting ui.fontSize must be a whole number from 12 to 28.")]
    [InlineData("invoice.roundOff", "\"yes\"", SettingLayer.Company, "Setting invoice.roundOff must be true or false.")]
    [InlineData("connection.host", "5", SettingLayer.Machine, "Setting connection.host must be text.")]
    [InlineData("ui.fontSize", "{", SettingLayer.User, "Setting ui.fontSize needs a JSON value.")]
    public void Validate_names_the_problem(string key, string json, SettingLayer layer, string message) =>
        Assert.Equal(message, SettingsCatalog.Validate(key, json, layer));

    [Theory]
    [InlineData("ui.fontSize", "28", SettingLayer.User)]
    [InlineData("ui.fontFamily", "\"Tahoma\"", SettingLayer.Machine)]
    [InlineData("invoice.roundOff", "false", SettingLayer.Company)]
    [InlineData("connection.host", "\"pc-1\"", SettingLayer.Machine)]
    public void Validate_accepts_good_values(string key, string json, SettingLayer layer) =>
        Assert.Null(SettingsCatalog.Validate(key, json, layer));

    [Fact]
    public void Every_default_is_valid_for_its_own_scope()
    {
        foreach (var d in SettingsCatalog.All)
            foreach (var (layer, scope) in new[] { (SettingLayer.Machine, SettingScope.Machine), (SettingLayer.Company, SettingScope.Company), (SettingLayer.User, SettingScope.User) })
                if (d.Scope.HasFlag(scope))
                    Assert.Null(SettingsCatalog.Validate(d.Key, d.DefaultJson, layer));
    }

    [Fact]
    public void Search_matches_keys_and_description_words()
    {
        Assert.Equal(["ui.fontFamily", "ui.fontSize"], SettingsCatalog.Search("font").Select(d => d.Key));
        Assert.Contains(SettingsCatalog.Search("ROUND"), d => d.Key == "invoice.roundOff");
        Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.Search(" ").Count);
    }

    [Fact]
    public void Category_is_the_text_before_the_first_dot() =>
        Assert.Equal("ui", SettingsCatalog.Find("ui.fontSize")!.Category);
    [Theory]
    [InlineData("\"60,30\"")]
    [InlineData("\"10,20,30,40,50,60\"")]
    public void Ageing_setting_rejects_falling_or_too_many_days(string json) =>
        Assert.Equal("Setting outstanding.ageingDays must be 1 to 5 rising whole numbers of days, such as 30,60,90.",
            SettingsCatalog.Validate("outstanding.ageingDays", json, SettingLayer.Company));

    [Fact]
    public void Ageing_setting_is_a_company_text_with_default_30_60_90()
    {
        Assert.Null(SettingsCatalog.Validate("outstanding.ageingDays", "\"45\"", SettingLayer.Company));
        var d = SettingsCatalog.Find("outstanding.ageingDays")!;
        Assert.Equal((SettingType.Text, "\"30,60,90\"", SettingScope.Company, "Ageing columns in OUTS, as days overdue."),
            (d.Type, d.DefaultJson, d.Scope, d.Description));
    }

    [Fact]
    public void PricesIncludeTax_is_a_company_boolean()
    {
        var d = SettingsCatalog.Find("invoice.pricesIncludeTax")!;
        Assert.Equal((SettingType.Boolean, "false", SettingScope.Company, "New invoice lines take prices with tax included."),
            (d.Type, d.DefaultJson, d.Scope, d.Description));
    }

    [Fact]
    public void Invoice_print_settings_validate()
    {
        Assert.Equal("Setting invoice.copies must be one of: byRule, original.", SettingsCatalog.Validate("invoice.copies", "\"all\"", SettingLayer.Company));
        Assert.Null(SettingsCatalog.Validate("invoice.copies", "\"original\"", SettingLayer.Company));
        var terms = System.Text.Json.JsonSerializer.Serialize(new string('x', 501));
        Assert.Equal("Setting invoice.terms must be at most 500 characters.", SettingsCatalog.Validate("invoice.terms", terms, SettingLayer.Company));
        Assert.Null(SettingsCatalog.Validate("invoice.bankDetails", System.Text.Json.JsonSerializer.Serialize("HDFC Bank\nA/c 123\nIFSC HDFC0001"), SettingLayer.Company));
        Assert.Equal("Setting invoice.signatory must be at most 100 characters.",
            SettingsCatalog.Validate("invoice.signatory", System.Text.Json.JsonSerializer.Serialize(new string('x', 101)), SettingLayer.Company));
        Assert.Null(SettingsCatalog.Validate("invoice.showPrice", "false", SettingLayer.Company));
        Assert.Null(SettingsCatalog.Validate("invoice.showDiscount", "true", SettingLayer.Company));
        Assert.Equal("Setting invoice.showPrice cannot be set for a user.", SettingsCatalog.Validate("invoice.showPrice", "true", SettingLayer.User));
        Assert.Equal("\"Authorised signatory\"", SettingsCatalog.Find("invoice.signatory")!.DefaultJson);
    }
}
