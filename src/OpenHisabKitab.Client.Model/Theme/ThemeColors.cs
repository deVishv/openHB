namespace OpenHisabKitab.Client.Model.Theme;

/// <summary>
/// The colours of the Windows 98 look (spec 6.6), in one place: the WPF theme builds every brush from these, and the
/// contrast tests check these same values.
/// </summary>
public sealed record ThemeColors(string Face, string Text, string Content, string ContentText, string Selection, string SelectionText,
    string DisabledText, string InactiveTitle, string InactiveTitleText, string Focus, string FocusOnSelection)
{
    // #404040 replaces the classic embossed grey disabled text and the #808080 inactive title bar, which fail AA.
    public static ThemeColors Classic { get; } = new(
        Face: "#C0C0C0", Text: "#000000", Content: "#FFFFFF", ContentText: "#000000", Selection: "#000080", SelectionText: "#FFFFFF",
        DisabledText: "#404040", InactiveTitle: "#404040", InactiveTitleText: "#FFFFFF", Focus: "#000000", FocusOnSelection: "#FFFFFF");
}
