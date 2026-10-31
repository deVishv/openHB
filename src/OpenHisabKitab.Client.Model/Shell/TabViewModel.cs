using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>One tab: its screen, the command that opened it, and its own back and forward history (spec 6.5, Alt+Left).</summary>
public sealed partial class TabViewModel : ObservableObject
{
    readonly Stack<string> back = new();
    readonly Stack<string> forward = new();

    internal TabViewModel(IScreen screen, string command)
    {
        Screen = screen;
        Command = command;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial int Number { get; internal set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial IScreen Screen { get; private set; }

    public string Command { get; private set; }

    /// <summary>Data under the screen changed since it loaded (spec 4, the "Changed" marker).</summary>
    [ObservableProperty]
    public partial bool Changed { get; set; }

    /// <summary>"2 LEDG HDFC Bank" (spec 6.1).</summary>
    public string Title => Screen.Subject.Length == 0 ? $"{Number} {Screen.Code}" : $"{Number} {Screen.Code} {Screen.Subject}";

    public bool CanGoBack => back.Count > 0;

    public bool CanGoForward => forward.Count > 0;

    internal void Replace(IScreen screen, string command)
    {
        back.Push(Command);
        forward.Clear();
        Show(screen, command);
    }

    internal string? PopBack() => back.Count > 0 ? back.Pop() : null;

    internal string? PopForward() => forward.Count > 0 ? forward.Pop() : null;

    internal void ShowFromBack(IScreen screen, string command)
    {
        forward.Push(Command);
        Show(screen, command);
    }

    internal void ShowFromForward(IScreen screen, string command)
    {
        back.Push(Command);
        Show(screen, command);
    }

    void Show(IScreen screen, string command)
    {
        Screen = screen;
        Command = command;
        Changed = false;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }
}
