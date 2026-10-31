using System.Windows;
using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

/// <summary>Passwords are read from their boxes at the moment of use and the boxes cleared.</summary>
public partial class UsersView : UserControl
{
    public UsersView() => InitializeComponent();

    UsersViewModel Vm => (UsersViewModel)DataContext;

    async void OnChangePassword(object sender, RoutedEventArgs e)
    {
        var (current, next, confirm) = (CurrentPassword.Password, NewPassword.Password, ConfirmPassword.Password);
        CurrentPassword.Clear();
        NewPassword.Clear();
        ConfirmPassword.Clear();
        await Vm.ChangePasswordAsync(current, next, confirm);
    }

    async void OnCreateToken(object sender, RoutedEventArgs e)
    {
        await Vm.CreateTokenAsync();
        if (Vm.CreatedToken is not null)
        {
            CreatedToken.Focus();
            CreatedToken.SelectAll();
        }
    }

    async void OnRevoke(object sender, RoutedEventArgs e)
    {
        if (TokenList.SelectedItem is TokenRow row) await Vm.RevokeAsync(row);
    }

    async void OnCreateUser(object sender, RoutedEventArgs e)
    {
        var password = UserPassword.Password;
        UserPassword.Clear();
        await Vm.CreateUserAsync(password);
    }

    async void OnSaveCompanies(object sender, RoutedEventArgs e) => await Vm.SaveCompaniesAsync();
}
