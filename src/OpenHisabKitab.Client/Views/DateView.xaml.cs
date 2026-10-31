using System.Windows;
using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class DateView : UserControl
{
    public DateView() => InitializeComponent();

    async void OnApply(object sender, RoutedEventArgs e) => await ((DateViewModel)DataContext).SaveAsync();
}
