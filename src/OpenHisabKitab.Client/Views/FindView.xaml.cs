using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class FindView : UserControl
{
    public FindView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is FindViewModel old) old.FindRequested -= FocusQuery;
            if (e.NewValue is not FindViewModel vm) return;
            vm.FindRequested += FocusQuery;
            // FIND alone starts in the query box; FIND with text starts in the results.
            Dispatcher.BeginInvoke(() =>
            {
                if (vm.Rows.Count > 0) FocusFirstResult();
                else FocusQuery();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    FindViewModel Vm => (FindViewModel)DataContext;

    void FocusQuery() => Query.Focus();

    void FocusFirstResult()
    {
        Results.SelectedIndex = 0;
        Results.UpdateLayout();
        (Results.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem)?.Focus();
    }

    async void OnSearch(object sender, RoutedEventArgs e)
    {
        await Vm.SearchAsync();
        if (Vm.Rows.Count > 0) FocusFirstResult();
    }

    async void OnMore(object sender, RoutedEventArgs e) => await Vm.MoreAsync();

    async void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await Vm.SearchAsync();
        if (Vm.Rows.Count > 0) FocusFirstResult();
    }

    /// <summary>Enter opens in this tab, Shift+Enter in a new one; PageDown on the last row loads the next page.</summary>
    async void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Results.SelectedItem is FoundRow row)
        {
            e.Handled = true;
            await Vm.OpenAsync(row, newTab: Keyboard.Modifiers == ModifierKeys.Shift);
        }
        else if (e.Key == Key.PageDown && Results.SelectedIndex == Vm.Rows.Count - 1 && Vm.HasMore)
        {
            e.Handled = true;
            await Vm.MoreAsync();
        }
    }

    async void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is FoundRow row) await Vm.OpenAsync(row, newTab: false);
    }
}
