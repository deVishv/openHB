using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using OpenHisabKitab.Client.Model.Screens.Reports;

namespace OpenHisabKitab.Client.Views;

/// <summary>
/// Shows any report screen: the grid's columns come from the screen's column list, so one view serves every report.
/// Enter opens a line in this tab and Shift+Enter in a new one; PageDown on the last line loads the next page.
/// </summary>
public partial class ReportView : UserControl
{
    static readonly DepthIndent Indent = new();

    public ReportView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReportScreen old)
            {
                old.PropertyChanged -= OnScreenChanged;
                old.ExportRequested -= SaveCsv;
                old.Loaded -= FocusFirstLine;
            }
            if (e.NewValue is not ReportScreen vm) return;
            vm.PropertyChanged += OnScreenChanged;
            vm.ExportRequested += SaveCsv;
            vm.Loaded += FocusFirstLine;
            BuildColumns(vm.Columns);
            Dispatcher.BeginInvoke(FocusFirstLine, System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    ReportScreen Vm => (ReportScreen)DataContext;

    void OnScreenChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportScreen.Columns)) BuildColumns(Vm.Columns);
    }

    void BuildColumns(IReadOnlyList<ReportColumn> columns)
    {
        Grid.Columns.Clear();
        for (var i = 0; i < columns.Count; i++)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{i}].Text"));
            if (columns[i].Align == CellAlign.Right)
            {
                text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
                text.SetResourceReference(TextBlock.FontFamilyProperty, "NumberFont");
            }
            if (i == 0) text.SetBinding(MarginProperty, new Binding(nameof(ReportLine.Depth)) { Converter = Indent });
            Grid.Columns.Add(new GridViewColumn
            {
                Header = columns[i].Header,
                Width = i == 0 ? 260 : 130,
                CellTemplate = new DataTemplate { VisualTree = text },
            });
        }
    }

    void FocusFirstLine()
    {
        if (Lines.Items.Count == 0) return;
        Lines.SelectedIndex = 0;
        Lines.UpdateLayout();
        (Lines.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem)?.Focus();
    }

    void SaveCsv()
    {
        var dialog = new SaveFileDialog { FileName = Vm.ExportName, DefaultExt = ".csv", Filter = Model.Resources.Text.CsvFileFilter, AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        using var file = File.Create(dialog.FileName);
        Vm.WriteCsv(file);
    }

    async void OnExport(object sender, RoutedEventArgs e) => await Vm.RunAsync("report.export");

    async void OnMore(object sender, RoutedEventArgs e) => await Vm.MoreAsync();

    async void OnLinesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Lines.SelectedItem is ReportLine line)
        {
            e.Handled = true;
            await Vm.OpenAsync(line, newTab: Keyboard.Modifiers == ModifierKeys.Shift);
        }
        else if (e.Key == Key.PageDown && Lines.SelectedIndex == Vm.Lines.Count - 1 && Vm.HasMore)
        {
            e.Handled = true;
            await Vm.MoreAsync();
        }
    }

    async void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Lines.SelectedItem is ReportLine line) await Vm.OpenAsync(line, newTab: false);
    }
}
