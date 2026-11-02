using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

/// <summary>AUDT: Enter in a filter searches; Enter on a record moves to its After pane.</summary>
public partial class AuditView : UserControl
{
    public AuditView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(() => From.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    AuditViewModel Vm => (AuditViewModel)DataContext;

    async void OnSearch(object sender, RoutedEventArgs e) => await Vm.SearchAsync();

    async void OnVerify(object sender, RoutedEventArgs e) => await Vm.VerifyAsync();

    async void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Vm.SearchAsync();
    }

    void OnEntriesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Entries.SelectedItem is null) return;
        e.Handled = true;
        After.Focus();
    }
}
