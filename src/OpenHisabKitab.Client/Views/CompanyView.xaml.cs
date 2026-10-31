using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class CompanyView : UserControl
{
    public CompanyView() => InitializeComponent();

    CompanyViewModel Vm => (CompanyViewModel)DataContext;

    async void OnOpen(object sender, RoutedEventArgs e) => await Vm.OpenAsync();

    async void OnCreate(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnEdit(object sender, RoutedEventArgs e)
    {
        await Vm.EditSelectedAsync();
        if (Vm.Editing) NewName.Focus();
    }

    async void OnAddYear(object sender, RoutedEventArgs e) => await Vm.AddYearAsync();

    /// <summary>Enter on a company opens it.</summary>
    async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Vm.OpenAsync();
    }
}
