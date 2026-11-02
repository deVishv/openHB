using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>Every function that has a screen in this build. A catalog code missing here says "{CODE} is not in this version yet.".</summary>
public static class ScreenCatalog
{
    public static Dictionary<string, ScreenFactory> Built() => new()
    {
        ["HELP"] = Now(HelpViewModel.Create),
        ["DATE"] = Now(DateViewModel.Create),
        ["CMPY"] = CompanyViewModel.CreateAsync,
        ["SETG"] = Now(SettingsViewModel.Create),
        ["KEYS"] = Now(KeysViewModel.Create),
        ["USER"] = Now(UsersViewModel.Create),
        ["ACCT"] = Now(AccountsViewModel.Create),
        ["ITEM"] = Now(ItemsViewModel.Create),
        ["PRTY"] = Now((cmd, ctx) => PartiesViewModel.Create(cmd, ctx)),
        ["FIND"] = FindViewModel.CreateAsync,
        ["LOCK"] = Now(LockViewModel.Create),
        ["TRLB"] = Now(Reports.TrialBalanceViewModel.Create),
        ["LEDG"] = Now(Reports.LedgerViewModel.Create),
        ["DAYB"] = Now(Reports.DayBookViewModel.Create),
        ["PNLS"] = Now(Reports.StatementViewModel.Create),
        ["BSHT"] = Now(Reports.StatementViewModel.Create),
        ["SALE"] = Entry.InvoiceEntryViewModel.CreateAsync,
        ["PURC"] = Entry.InvoiceEntryViewModel.CreateAsync,
        ["CRNT"] = Entry.InvoiceEntryViewModel.CreateAsync,
        ["DBNT"] = Entry.InvoiceEntryViewModel.CreateAsync,
        ["RCPT"] = Entry.JournalEntryViewModel.CreateAsync,
        ["PYMT"] = Entry.JournalEntryViewModel.CreateAsync,
        ["JRNL"] = Entry.JournalEntryViewModel.CreateAsync,
        ["CNTR"] = Entry.JournalEntryViewModel.CreateAsync,
    };

    /// <summary>A factory that needs nothing from the host.</summary>
    static ScreenFactory Now(Func<ParsedCommand, ShellContext, IScreen?> make) => (cmd, ctx) => Task.FromResult(make(cmd, ctx));
}
