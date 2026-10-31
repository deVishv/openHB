using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>The status bar (spec 6.1): short context only.</summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    /// <summary>The last one line message, such as "Unknown function: XXXX" (spec 6.2). Cleared by the next command.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    /// <summary>"Connected to pc-1:47250", "Host not reachable. Trying again.", or "Log in again.".</summary>
    [ObservableProperty]
    public partial string Connection { get; set; } = "";

    /// <summary>"Date 15-04-2026".</summary>
    [ObservableProperty]
    public partial string WorkingDate { get; set; } = "";

    /// <summary>The active tab's "Changed" marker (spec 4, Transport).</summary>
    [ObservableProperty]
    public partial bool Changed { get; set; }
}
