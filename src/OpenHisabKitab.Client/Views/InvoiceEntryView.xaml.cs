using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens.Entry;

namespace OpenHisabKitab.Client.Views;

public partial class InvoiceEntryView : UserControl
{
    public InvoiceEntryView() => InitializeComponent();

    InvoiceEntryViewModel Vm => (InvoiceEntryViewModel)DataContext;

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnBills(object sender, RoutedEventArgs e)
    {
        if (Vm.CanRun("voucher.bills")) await Vm.RunAsync("voucher.bills");
    }

    async void OnCancelVoucher(object sender, RoutedEventArgs e) => await Vm.CancelVoucherAsync();

    /// <summary>Leaving a price on a note that settles bills suggests them, the original invoice first.</summary>
    async void OnAmountCommitted(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Vm.IsNote) await Vm.SuggestBillsAsync();
    }

    async void OnCreateParty(object? sender, EventArgs e) => await Vm.CreatePartyAsync();
}
