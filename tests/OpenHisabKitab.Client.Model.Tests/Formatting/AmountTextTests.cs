using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Tests.Formatting;

public sealed class AmountTextTests
{
    static EffectiveSettings With(string? negative) => new(new Dictionary<SettingLayer, IReadOnlyDictionary<string, string>>
    {
        [SettingLayer.User] = negative is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["ui.negativeAmounts"] = negative },
    });

    [Fact]
    public void Amount_text_uses_brackets_or_minus_by_setting()
    {
        var m = new Money(-1234567_89);
        Assert.Equal("(12,34,567.89)", AmountText.Format(m, With(null)));
        Assert.Equal("-12,34,567.89", AmountText.Format(m, With("\"minus\"")));
        Assert.Equal("12,34,567.89", AmountText.Format(-m, With("\"minus\"")));
    }

    [Fact]
    public void Dr_cr_shows_the_side()
    {
        Assert.Equal("1,200.00 Dr", AmountText.DrCr(new Money(120000)));
        Assert.Equal("1,200.00 Cr", AmountText.DrCr(new Money(-120000)));
        Assert.Equal("0.00", AmountText.DrCr(Money.Zero));
    }
}
