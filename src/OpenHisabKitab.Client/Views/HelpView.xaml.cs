using System.Windows.Controls;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

public partial class HelpView : UserControl
{
    public HelpView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is HelpViewModel old) old.FocusFilterRequested -= FocusFilter;
            if (e.NewValue is HelpViewModel now) now.FocusFilterRequested += FocusFilter;
        };
    }

    void FocusFilter() => FilterBox.Focus();
}
