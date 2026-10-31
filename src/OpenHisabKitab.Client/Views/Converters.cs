using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenHisabKitab.Client.Views;

/// <summary>Visible when the bound enum value's name equals the parameter; with a parameter starting "!", when it differs.</summary>
public sealed class EnumVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = parameter as string ?? "";
        var negate = name.StartsWith('!');
        var match = string.Equals(value?.ToString(), negate ? name[1..] : name, StringComparison.Ordinal);
        return match != negate ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the value is not null and not an empty string.</summary>
public sealed class PresentVisibility : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A settings layer by its plain name: "This PC", "User".</summary>
public sealed class LayerName : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Core.Settings.SettingLayer layer ? Model.Screens.SettingsViewModel.LayerName(layer) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
