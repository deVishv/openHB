using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;

namespace OpenHisabKitab.Core.Tests.Formatting;

public sealed class AmountInWordsTests
{
    [Theory]
    [InlineData(123456789L, "Rupees Twelve Lakh Thirty Four Thousand Five Hundred Sixty Seven and Eighty Nine Paise Only")]
    [InlineData(0L, "Rupees Zero Only")]
    [InlineData(50L, "Rupees Zero and Fifty Paise Only")]
    [InlineData(100000000000L, "Rupees One Hundred Crore Only")]
    [InlineData(100L, "Rupees One Only")]
    [InlineData(1101L, "Rupees Eleven and One Paise Only")]
    [InlineData(10000000L, "Rupees One Lakh Only")]
    [InlineData(1234567890000L, "Rupees One Thousand Two Hundred Thirty Four Crore Fifty Six Lakh Seventy Eight Thousand Nine Hundred Only")]
    public void Words_use_lakh_and_crore(long paise, string words) => Assert.Equal(words, AmountInWords.Rupees(new Money(paise)));

    [Fact]
    public void Words_refuse_negative_amounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AmountInWords.Rupees(new Money(-1)));
}
