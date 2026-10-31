namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>
/// A screen with a keyboard entry grid (spec 6.4). The view tells it which line has focus, so line.add and line.remove act
/// on that line, and asks it for a new line when Enter leaves the last field of the last line.
/// </summary>
public interface IEntryLines
{
    object? CurrentLine { get; set; }

    /// <summary>Adds a line after the current one, or at the end, and returns it.</summary>
    object AddLine();
}
