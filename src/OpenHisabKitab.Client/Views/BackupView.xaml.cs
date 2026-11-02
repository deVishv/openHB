using System.Windows;
using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

/// <summary>BKUP: back up now, restore the selected file, and the Owner's backup schedule (Ctrl+S saves it).</summary>
public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (BackupNow.IsVisible) BackupNow.Focus();
            else Files.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    BackupViewModel Vm => (BackupViewModel)DataContext;

    async void OnBackupNow(object sender, RoutedEventArgs e) => await Vm.BackupNowAsync();

    async void OnRestore(object sender, RoutedEventArgs e) => await Vm.RestoreAsync();

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();
}
