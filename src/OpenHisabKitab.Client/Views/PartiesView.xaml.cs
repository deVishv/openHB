using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class PartiesView : UserControl
{
    public PartiesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PartiesViewModel old) old.FindRequested -= FocusFilter;
            if (e.NewValue is not PartiesViewModel vm) return;
            vm.FindRequested += FocusFilter;
            // The sub screen a voucher opens shows the form only, with the name already typed.
            var sub = vm.IsSubScreen;
            ListSide.Visibility = sub ? Visibility.Collapsed : Visibility.Visible;
            NewButton.Visibility = sub ? Visibility.Collapsed : Visibility.Visible;
            if (sub) Dispatcher.BeginInvoke(() => FormGroup.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    PartiesViewModel Vm => (PartiesViewModel)DataContext;

    void FocusFilter() => Filter.Focus();

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnNew(object sender, RoutedEventArgs e)
    {
        await Vm.RunAsync("record.new");
        FormName.Focus();
    }

    async void OnAddLine(object sender, RoutedEventArgs e) => await Vm.RunAsync("line.add");

    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FormName.Focus();
    }
}
