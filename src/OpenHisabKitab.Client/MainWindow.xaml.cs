using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using OpenHisabKitab.Client.Input;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Client.Model.Shell;

namespace OpenHisabKitab.Client;

/// <summary>
/// The shell window. Every key goes through the keybindings first (spec 6.5); letters typed with no field focused go to
/// the prompt (spec 6.2, Typing anywhere). The model holds all behaviour; this file only translates WPF events.
/// </summary>
public partial class MainWindow : Window
{
    readonly MachineFiles files;
    ShellViewModel shell = null!;

    public MainWindow(MachineFiles files)
    {
        this.files = files;
        InitializeComponent();
        RestorePlacement();
    }

    /// <summary>The shell needs this window for its Yes/No box, so it is made after the window and attached here.</summary>
    public void Attach(ShellViewModel model)
    {
        shell = model;
        DataContext = shell;
        shell.FocusPromptRequested += FocusPrompt;
        shell.Prompt.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PromptViewModel.Suggestions)) SuggestionPopup.IsOpen = shell.Prompt.Suggestions.Count > 0 && Prompt.IsKeyboardFocused;
        };
        shell.Palette.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PaletteViewModel.IsOpen))
                Dispatcher.BeginInvoke(() => { if (shell.Palette.IsOpen) PaletteSearch.Focus(); else FocusPrompt(); }, DispatcherPriority.Input);
        };
        shell.Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabsViewModel.Active)) Dispatcher.BeginInvoke(FocusContent, DispatcherPriority.Input);
        };
        shell.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SessionViewModel.Panel) && shell.Session.Panel == SessionPanel.None)
                Dispatcher.BeginInvoke(FocusPrompt, DispatcherPriority.Input);
        };
        shell.Context.Keys.Changed += ShowGestures;
        Prompt.LostKeyboardFocus += (_, _) => SuggestionPopup.IsOpen = false;
        ShowGestures();
    }

    async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (shell.Palette.IsOpen || KeyInput.From(e) is not { } chord) return;
        if (shell.WantsEveryKey)
        {
            // KEYS is recording: the key is the new binding, whatever it is bound to now.
            e.Handled = true;
            await shell.HandleKeyAsync(chord, new HashSet<string>());
            return;
        }
        // Marked handled before awaiting: WPF reads Handled when this handler returns, at the first await.
        var context = KeyInput.Context(Keyboard.FocusedElement, shell.Palette.IsOpen);
        if (shell.Context.Keys.Current.Resolve(chord, context) is null || !shell.AcceptsTyping) return;
        e.Handled = true;
        await shell.HandleKeyAsync(chord, context);
    }

    void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!shell.AcceptsTyping || shell.Palette.IsOpen || e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
        // A field takes its own typing, and an open menu takes its access keys (Alt, H, K reaches KEYS).
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox or MenuItem or Menu) return;
        shell.Prompt.Text += e.Text;
        FocusPrompt();
        e.Handled = true;
    }

    /// <summary>Up and Down move through suggestions, Tab completes one; Enter runs through its keybinding.</summary>
    void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        var p = shell.Prompt;
        if (p.Suggestions.Count == 0) return;
        switch (e.Key)
        {
            case Key.Down: p.SelectedSuggestion = Math.Min(p.SelectedSuggestion + 1, p.Suggestions.Count - 1); e.Handled = true; break;
            case Key.Up: p.SelectedSuggestion = Math.Max(p.SelectedSuggestion - 1, -1); e.Handled = true; break;
            case Key.Tab when p.SelectedSuggestion >= 0 || p.Suggestions.Count == 1:
                if (p.SelectedSuggestion < 0) p.SelectedSuggestion = 0;
                p.Complete();
                Prompt.CaretIndex = Prompt.Text.Length;
                e.Handled = true;
                break;
        }
    }

    async void OnPaletteKeyDown(object sender, KeyEventArgs e)
    {
        var p = shell.Palette;
        switch (e.Key)
        {
            case Key.Down: p.Selected = Math.Min(p.Selected + 1, p.Items.Count - 1); e.Handled = true; break;
            case Key.Up: p.Selected = Math.Max(p.Selected - 1, 0); e.Handled = true; break;
            case Key.Escape: p.Close(); e.Handled = true; break;
            case Key.Enter:
                e.Handled = true;
                await p.AcceptAsync();
                break;
        }
    }

    async void OnCommand(object sender, RoutedEventArgs e)
    {
        var command = (string)((FrameworkElement)sender).Tag;
        if (shell.Context.Keys.Current.KeyFor(command) is { } chord) await shell.HandleKeyAsync(chord, new HashSet<string>());
        else if (command.StartsWith("run.", StringComparison.Ordinal)) await shell.RunAsync(command[4..], newTab: false);
        else if (command == "palette.open") shell.Palette.Open();
    }

    async void OnFKey(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FKeyLabel { Code: { } code }) await shell.RunAsync(code, newTab: false);
    }

    async void OnLogOut(object sender, RoutedEventArgs e) => await shell.LogOutAsync();

    void OnExit(object sender, RoutedEventArgs e) => Close();

    /// <summary>Menu items show the keys in force, so a rebinding shows here too.</summary>
    void ShowGestures()
    {
        foreach (var item in new[] { FindItem, SaveItem, PaletteItem, NextItem, PreviousItem, HelpItem, KeysItem })
            item.InputGestureText = shell.Context.Keys.Current.KeyFor((string)item.Tag) is { } chord ? KeyText.Display(chord) : "";
    }

    void FocusPrompt()
    {
        Prompt.Focus();
        Prompt.CaretIndex = Prompt.Text.Length;
    }

    void FocusContent()
    {
        if (ScreenHost.Content is null) return;
        ScreenHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    void RestorePlacement()
    {
        if (files.LoadState().Window is not { } w) return;
        var area = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
        if (w.Left >= area || w.Width < MinWidth || w.Height < MinHeight) return;
        (Left, Top, Width, Height) = (w.Left, w.Top, w.Width, w.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        if (w.Maximised) WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        var r = RestoreBounds;
        files.SaveState(files.LoadState() with
        {
            Window = new WindowPlacement((int)r.Left, (int)r.Top, (int)r.Width, (int)r.Height, WindowState == WindowState.Maximized),
        });
        base.OnClosing(e);
    }
}
