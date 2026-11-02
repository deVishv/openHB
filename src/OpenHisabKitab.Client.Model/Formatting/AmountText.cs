using OpenHisabKitab.Core.Amounts;
using OpenHisabKitab.Core.Formatting;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Formatting;

/// <summary>Amounts as screens show them (spec 6.6): Indian grouping, negatives as ui.negativeAmounts says, Dr and Cr for either side.</summary>
public static class AmountText
{
    public static string Format(Money m, EffectiveSettings s) =>
        IndianNumberFormat.Format(m, s.Text("ui.negativeAmounts") == "minus" ? NegativeStyle.Minus : NegativeStyle.Brackets);

    public static string DrCr(Money m) => IndianNumberFormat.FormatDrCr(m);
}
