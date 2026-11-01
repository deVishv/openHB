using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using OpenHisabKitab.Client.Model.Screens;

namespace OpenHisabKitab.Client.Views;

/// <summary>
/// The keyboard entry grid of spec 6.4, as an attached behaviour on an ItemsControl of TextBox rows: Tab and Enter move to
/// the next field, Shift+Tab moves back, and Enter in the last field of the last row adds a row. The row holding focus is
/// told to the screen (IEntryLines.CurrentLine), so line.add and line.remove act on it. KeyInput marks focus inside it as gridFocus.
/// </summary>
public static class EntryGrid
{
    public static readonly DependencyProperty IsEntryGridProperty =
        DependencyProperty.RegisterAttached("IsEntryGrid", typeof(bool), typeof(EntryGrid), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEntryGrid(DependencyObject d) => (bool)d.GetValue(IsEntryGridProperty);

    public static void SetIsEntryGrid(DependencyObject d, bool value) => d.SetValue(IsEntryGridProperty, value);

    /// <summary>The entry grid holding an element, or null.</summary>
    public static ItemsControl? Owner(DependencyObject? d)
    {
        for (var x = d; x is not null; x = VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            if (x is ItemsControl ic && GetIsEntryGrid(ic)) return ic;
        return null;
    }

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl grid || e.NewValue is not true) return;
        KeyboardNavigation.SetTabNavigation(grid, KeyboardNavigationMode.Continue);
        grid.PreviewKeyDown += OnKeyDown;
        grid.GotKeyboardFocus += OnGotFocus;
    }

    static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var grid = (ItemsControl)sender;
        if (e.NewFocus is FrameworkElement { DataContext: { } line } && grid.DataContext is IEntryLines lines && grid.Items.Contains(line))
            lines.CurrentLine = line;
    }

    static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is not TextBox box) return;
        // A name box handles its own Enter: it commits first and moves on itself.
        if (FindParent<LookupBox>(box) is not null) return;
        var grid = (ItemsControl)sender;
        e.Handled = true;
        if (IsLastField(grid, box) && grid.DataContext is IEntryLines lines)
        {
            var line = lines.AddLine();
            grid.Dispatcher.BeginInvoke(() => FocusFirstField(grid, line), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    static bool IsLastField(ItemsControl grid, TextBox box)
    {
        if (grid.Items.Count == 0 || grid.ItemContainerGenerator.ContainerFromIndex(grid.Items.Count - 1) is not DependencyObject last) return false;
        return Fields(last).LastOrDefault() == box;
    }

    static void FocusFirstField(ItemsControl grid, object line)
    {
        if (grid.ItemContainerGenerator.ContainerFromItem(line) is DependencyObject row) Fields(row).FirstOrDefault()?.Focus();
    }

    static IEnumerable<TextBox> Fields(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { IsTabStop: true, IsReadOnly: false } t && t is not { Visibility: not Visibility.Visible }) yield return t;
            if (child is Popup) continue;
            foreach (var f in Fields(child)) yield return f;
        }
    }

    static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var x = VisualTreeHelper.GetParent(d); x is not null; x = VisualTreeHelper.GetParent(x))
            if (x is T found) return found;
        return null;
    }
}
