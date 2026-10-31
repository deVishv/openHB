using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenHisabKitab.Client.Model.Screens.Entry;

namespace OpenHisabKitab.Client.Views;

public partial class JournalEntryView : UserControl
{
    public JournalEntryView() => InitializeComponent();

    JournalEntryViewModel Vm => (JournalEntryViewModel)DataContext;

    async void OnSave(object sender, RoutedEventArgs e) => await Vm.SaveAsync();

    async void OnBills(object sender, RoutedEventArgs e)
    {
        if (Vm.CanRun("voucher.bills")) await Vm.RunAsync("voucher.bills");
    }

    async void OnCancelVoucher(object sender, RoutedEventArgs e) => await Vm.CancelVoucherAsync();

    /// <summary>Leaving a party line's amount suggests its bills, oldest first.</summary>
    async void OnAmountCommitted(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EntryLine line }) await Vm.LineAmountCommittedAsync(line);
    }

    /// <summary>A name with no match: PRTY opens over the voucher with the typed name (spec 6.4).</summary>
    async void OnCreateParty(object? sender, EventArgs e)
    {
        if (sender is FrameworkElement { DataContext: { } field } box && FindLine(box) is { } line && ReferenceEquals(line.Ledger, field))
            await Vm.CreatePartyAsync(line);
    }

    static EntryLine? FindLine(DependencyObject d)
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x))
            if (x is FrameworkElement { DataContext: EntryLine line }) return line;
        return null;
    }
}
