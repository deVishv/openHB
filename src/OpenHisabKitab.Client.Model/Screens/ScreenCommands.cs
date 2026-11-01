namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>
/// A screen that takes the record and line commands (record.new, record.delete, line.add, line.remove, voucher.bills,
/// voucher.cancelVoucher). The shell sends a command only when CanRun says the screen takes it now.
/// </summary>
public interface IScreenCommands
{
    bool CanRun(string command);

    Task RunAsync(string command);
}

/// <summary>A choice in a list: the value the screen keeps, and the text the user sees.</summary>
public sealed record ChoiceOption(string Value, string Label);
