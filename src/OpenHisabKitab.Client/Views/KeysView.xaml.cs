using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class KeysView : UserControl
{
    public KeysView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is KeysViewModel old) old.FocusFilterRequested -= FocusFilter;
            if (e.NewValue is KeysViewModel now) now.FocusFilterRequested += FocusFilter;
        };
    }

    KeysViewModel Vm => (KeysViewModel)DataContext;

    void FocusFilter() => FilterBox.Focus();

    void OnRecord(object sender, RoutedEventArgs e)
    {
        Vm.StartRecording();
        KeyList.Focus();
    }

    async void OnReset(object sender, RoutedEventArgs e)
    {
        if (Vm.Selected is { } row) await Vm.ResetAsync(row);
    }

    async void OnSaveJson(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Vm.StartRecording();
                break;
            case Key.Delete when Vm.Selected is { } row:
                e.Handled = true;
                await Vm.ResetAsync(row);
                break;
        }
    }
}
