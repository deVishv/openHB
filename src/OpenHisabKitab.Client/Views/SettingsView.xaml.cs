using System.Windows;
using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsViewModel old) old.FocusFilterRequested -= FocusFilter;
            if (e.NewValue is SettingsViewModel now) now.FocusFilterRequested += FocusFilter;
        };
    }

    SettingsViewModel Vm => (SettingsViewModel)DataContext;

    void FocusFilter() => FilterBox.Focus();

    void OnSelected(object sender, SelectionChangedEventArgs e) =>
        ValueBox.Text = SettingList.SelectedItem is SettingRow row ? row.ValueText : "";

    void OnHostSelected(object sender, SelectionChangedEventArgs e) =>
        HostValue.Text = HostList.SelectedItem is HostSettingRow row ? row.Value : "";

    async void OnSet(object sender, RoutedEventArgs e)
    {
        if (SettingList.SelectedItem is SettingRow row) await Vm.SetValueAsync(row, ValueBox.Text);
    }

    async void OnReset(object sender, RoutedEventArgs e)
    {
        if (SettingList.SelectedItem is SettingRow row) await Vm.ResetAsync(row);
    }

    async void OnSetHost(object sender, RoutedEventArgs e)
    {
        if (HostList.SelectedItem is HostSettingRow row) await Vm.SetHostAsync(row, HostValue.Text.Trim());
    }

    async void OnSaveJson(object sender, RoutedEventArgs e) => await Vm.SaveAsync();
}
