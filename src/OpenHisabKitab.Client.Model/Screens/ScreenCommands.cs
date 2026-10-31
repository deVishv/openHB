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
