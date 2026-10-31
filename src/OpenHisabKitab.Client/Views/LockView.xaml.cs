using System.Windows;
using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class LockView : UserControl
{
    public LockView()
    {
        InitializeComponent();
        Loaded += (_, _) => NewDate.Focus();
    }

    LockViewModel Vm => (LockViewModel)DataContext;

    async void OnLock(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnUnlock(object sender, RoutedEventArgs e) => await Vm.UnlockAsync();
}
