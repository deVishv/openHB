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
}
