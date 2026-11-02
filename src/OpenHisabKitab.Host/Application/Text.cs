using System.Globalization;
using OpenHisabKitab.Core.Gst;

namespace OpenHisabKitab.Host.Application;

/// <summary>How values appear inside messages: dates as dd-mm-yyyy and rates as plain percentages.</summary>
internal static class Text
{
    public static string Date(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static string Rate(GstRate r) => (r.BasisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    public static string Key(long id) => id.ToString(CultureInfo.InvariantCulture);

    public static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>
    /// The words for report lines that have no chart name, so REST and MCP readers see what the screens show. These
    /// match the client's Text.resx; a client test compares them.
    /// </summary>
    public static string Label(Core.Reports.LayoutLine line) => line.Kind switch
    {
        Core.Reports.LayoutLineKind.CashAndEquivalents => "Cash and cash equivalents",
        Core.Reports.LayoutLineKind.ProfitForPeriod => "Profit for the period",
        Core.Reports.LayoutLineKind.OpeningDifference => "Opening difference",
        Core.Reports.LayoutLineKind.SideTotal => "Total",
        Core.Reports.LayoutLineKind.HeadingTotal => $"Total for {line.Label}",
        Core.Reports.LayoutLineKind.NetProfit when line.Previous is { } p && (p < Core.Amounts.Money.Zero) != (line.Current < Core.Amounts.Money.Zero)
            => "Net profit / (loss)",
        Core.Reports.LayoutLineKind.NetProfit => line.Current < Core.Amounts.Money.Zero ? "Net loss" : "Net profit",
        _ => line.Label,
    };

    public static string Label(Core.Reports.CashFlowLine line, string group) => line.Item switch
    {
        Core.Reports.CashFlowItem.NetProfit => line.Amount < Core.Amounts.Money.Zero ? "Net loss" : "Net profit",
        Core.Reports.CashFlowItem.Depreciation => "Add: depreciation",
        _ => $"Change in {group}",
    };
}
