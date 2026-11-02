using OpenHisabKitab.Core.Amounts;

namespace OpenHisabKitab.Client.Model.Screens.Reports;

public enum CellAlign { Left, Right }

/// <summary>A report column: its header and how its cells line up.</summary>
public sealed record ReportColumn(string Header, CellAlign Align);

/// <summary>One cell as shown. Amount, when set, is what CSV writes instead of the shown text.</summary>
public sealed record ReportCell(string Text, Money? Amount = null);

/// <summary>A report line: its indent, cells, the command Enter runs (or null), and whether it is a total.</summary>
public sealed record ReportLine(int Depth, IReadOnlyList<ReportCell> Cells, string? Drill, bool Emphasis)
{
    /// <summary>The line as a screen reader announces it.</summary>
    public string Spoken => string.Join(", ", Cells.Select(c => c.Text).Where(t => t.Length > 0));
}
