using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
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

    async void OnChooseLogo(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Model.Resources.Text.PngFileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await Vm.SetLogoAsync(dialog.FileName);
    }

    async void OnRemoveLogo(object sender, RoutedEventArgs e) => await Vm.RemoveLogoAsync();

    /// <summary>Enter on a company opens it.</summary>
    async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Vm.OpenAsync();
    }
}
