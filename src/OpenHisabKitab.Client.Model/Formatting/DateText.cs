using System.Globalization;

namespace OpenHisabKitab.Client.Model.Formatting;

/// <summary>Dates as the ui.dateFormat setting shows them (spec 6.6, Dates).</summary>
public static class DateText
{
    public static string Format(DateOnly d, string dateFormat) => d.ToString(dateFormat switch
    {
        "dd/mm/yyyy" => "dd'/'MM'/'yyyy",
        "yyyy-mm-dd" => "yyyy-MM-dd",
        _ => "dd-MM-yyyy",
    }, CultureInfo.InvariantCulture);
}
