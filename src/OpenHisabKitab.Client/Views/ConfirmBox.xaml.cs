using System.Windows;
using OpenHisabKitab.Client.Model.Shell;

namespace OpenHisabKitab.Client.Views;

public partial class ConfirmBox : Window
{
    public ConfirmBox(string question)
    {
        InitializeComponent();
        Question.Text = question;
    }

    void OnYes(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>Asks in a keyboard Yes/No box over the main window. Enter and Esc both mean No, so nothing is discarded by accident.</summary>
public sealed class DialogConfirm(Window owner) : IConfirm
{
    public Task<bool> AskAsync(string question) =>
        Task.FromResult(new ConfirmBox(question) { Owner = owner }.ShowDialog() == true);
}
