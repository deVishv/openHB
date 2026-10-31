using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>The status bar (spec 6.1): short context only.</summary>
public sealed partial class StatusBarViewModel : ObservableObject
{
    /// <summary>The last one line message, such as "Unknown function: XXXX" (spec 6.2). Cleared by the next command.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }
}
