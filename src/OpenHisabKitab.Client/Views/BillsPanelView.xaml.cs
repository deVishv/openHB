using System.Windows.Controls;

namespace OpenHisabKitab.Client.Views;

public partial class BillsPanelView : UserControl
{
    public BillsPanelView()
    {
        InitializeComponent();
        // The first amount takes focus as the panel opens, so the keyboard goes straight to allocating.
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)),
            System.Windows.Threading.DispatcherPriority.Input);
    }
}
