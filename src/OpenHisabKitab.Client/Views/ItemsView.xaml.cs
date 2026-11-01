using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class ItemsView : UserControl
{
    public ItemsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ItemsViewModel old) old.FindRequested -= FocusFilter;
            if (e.NewValue is ItemsViewModel vm) vm.FindRequested += FocusFilter;
        };
    }

    ItemsViewModel Vm => (ItemsViewModel)DataContext;

    void FocusFilter() => Filter.Focus();

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnNew(object sender, RoutedEventArgs e)
    {
        await Vm.RunAsync("record.new");
        FormName.Focus();
    }

    void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        FormName.Focus();
    }
}
