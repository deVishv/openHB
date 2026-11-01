using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class AccountsView : UserControl
{
    public AccountsView() => InitializeComponent();

    AccountsViewModel Vm => (AccountsViewModel)DataContext;

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnNew(object sender, RoutedEventArgs e)
    {
        await Vm.RunAsync("record.new");
        FormName.Focus();
    }

    async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Vm.CanRun("record.delete")) await Vm.RunAsync("record.delete");
    }

    /// <summary>The selected row stays in view, so a ledger just saved shows in the long chart.</summary>
    void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ChartList.SelectedItem is { } row) ChartList.ScrollIntoView(row);
    }

    /// <summary>Enter on a row moves to the form to edit it.</summary>
    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FormName.Focus();
        FormName.SelectAll();
    }
}
