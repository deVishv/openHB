using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenHisabKitab.Client.Model.Shell;

namespace OpenHisabKitab.Client.Views;

/// <summary>The connect, trust, setup, and login panels. Passwords are read from the boxes at the moment of use and cleared.</summary>
public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old) old.PropertyChanged -= OnChanged;
            if (e.NewValue is INotifyPropertyChanged now) now.PropertyChanged += OnChanged;
            FocusFirst();
        };
    }

    SessionViewModel Vm => (SessionViewModel)DataContext;

    async void OnConnect(object sender, RoutedEventArgs e) => await Vm.ConnectAsync();

    async void OnTrust(object sender, RoutedEventArgs e) => await Vm.TrustAsync();

    void OnChangeHost(object sender, RoutedEventArgs e) => Vm.ChangeHost();

    async void OnSetup(object sender, RoutedEventArgs e)
    {
        var (password, confirm) = (SetupPassword.Password, SetupConfirm.Password);
        SetupPassword.Clear();
        SetupConfirm.Clear();
        await Vm.SetupAsync(password, confirm);
    }

    async void OnLogin(object sender, RoutedEventArgs e)
    {
        var password = LoginPassword.Password;
        LoginPassword.Clear();
        await Vm.LoginAsync(password);
    }

    void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionViewModel.Panel)) Dispatcher.BeginInvoke(FocusFirst, DispatcherPriority.Input);
    }

    /// <summary>Each panel starts at its first field, so the whole flow works from the keyboard.</summary>
    void FocusFirst()
    {
        if (DataContext is not SessionViewModel vm) return;
        Control? first = vm.Panel switch
        {
            SessionPanel.Connect => HostName,
            SessionPanel.Setup => string.IsNullOrEmpty(vm.UserName) ? SetupUser : SetupPassword,
            SessionPanel.Login => string.IsNullOrEmpty(vm.UserName) ? LoginUser : LoginPassword,
            _ => null,
        };
        if (first is not null) first.Focus();
        else MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
    }
}
