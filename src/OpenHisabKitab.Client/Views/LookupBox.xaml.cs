using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenHisabKitab.Client.Views;

/// <summary>
/// The view of a Client.Model LookupField. It binds to the field's Text and Suggestions and calls its Commit and Pick, so
/// one control serves ledger, party, group, and item fields. DisplayPath names the item property shown in the list.
/// </summary>
public partial class LookupBox : UserControl
{
    public static readonly DependencyProperty AutomationNameProperty =
        DependencyProperty.Register(nameof(AutomationName), typeof(string), typeof(LookupBox), new PropertyMetadata(""));

    public static readonly DependencyProperty DisplayPathProperty =
        DependencyProperty.Register(nameof(DisplayPath), typeof(string), typeof(LookupBox),
            new PropertyMetadata("Name", (d, e) => ((LookupBox)d).Choices.DisplayMemberPath = (string)e.NewValue));

    INotifyPropertyChanged? field;

    public LookupBox()
    {
        InitializeComponent();
        Choices.DisplayMemberPath = DisplayPath;
        DataContextChanged += (_, _) => Watch(DataContext as INotifyPropertyChanged);
        GotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus == this) Input.Focus();
        };
    }

    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    public string DisplayPath
    {
        get => (string)GetValue(DisplayPathProperty);
        set => SetValue(DisplayPathProperty, value);
    }

    /// <summary>Raised after Enter or Tab committed the text with no match, so the screen can offer to create one.</summary>
    public event EventHandler? CreateRequested;

    /// <summary>Raised after a commit that found a single match or cleared the field.</summary>
    public event EventHandler? Committed;

    void Watch(INotifyPropertyChanged? next)
    {
        if (field is not null) field.PropertyChanged -= OnFieldChanged;
        field = next;
        if (field is not null) field.PropertyChanged += OnFieldChanged;
    }

    void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "Suggestions") return;
        Suggest.IsOpen = Input.IsKeyboardFocused && Suggestions().Count > 0;
    }

    ICollection Suggestions() => (DataContext?.GetType().GetProperty("Suggestions")?.GetValue(DataContext) as ICollection) ?? Array.Empty<object>();

    bool Commit() => DataContext?.GetType().GetMethod("Commit")?.Invoke(DataContext, null) is true;

    void Pick(object item) => DataContext?.GetType().GetMethod("Pick")?.Invoke(DataContext, [item]);

    bool OffersCreate() => DataContext?.GetType().GetProperty("OffersCreate")?.GetValue(DataContext) is true;

    void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when Suggestions().Count > 0:
                Suggest.IsOpen = true;
                Choices.SelectedIndex = 0;
                (Choices.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab:
                if (Commit())
                {
                    Suggest.IsOpen = false;
                    Committed?.Invoke(this, EventArgs.Empty);
                    // Enter moves on like Tab (spec 6.4); Tab moves on by itself.
                    if (e.Key == Key.Enter) MoveOn(e);
                    return;
                }
                e.Handled = true;
                if (OffersCreate()) CreateRequested?.Invoke(this, EventArgs.Empty);
                else if (Suggestions().Count > 0) Suggest.IsOpen = true;
                break;
            case Key.Escape when Suggest.IsOpen:
                Suggest.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    void OnChoicesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Choices.SelectedItem is { } item)
        {
            PickAndReturn(item);
            MoveOn(e);
        }
        else if (e.Key == Key.Escape)
        {
            Suggest.IsOpen = false;
            Input.Focus();
            e.Handled = true;
        }
    }

    void OnChoicesDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Choices.SelectedItem is { } item) PickAndReturn(item);
    }

    void PickAndReturn(object item)
    {
        Pick(item);
        Suggest.IsOpen = false;
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;
        Committed?.Invoke(this, EventArgs.Empty);
    }

    void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject d && IsInside(d)) return;
        Suggest.IsOpen = false;
    }

    bool IsInside(DependencyObject d)
    {
        for (var x = d; x is not null; x = System.Windows.Media.VisualTreeHelper.GetParent(x) ?? LogicalTreeHelper.GetParent(x))
            if (x == Choices) return true;
        return false;
    }

    void MoveOn(KeyEventArgs e)
    {
        e.Handled = true;
        Input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }
}
