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
    };

    /// <summary>A factory that needs nothing from the host.</summary>
    static ScreenFactory Now(Func<ParsedCommand, ShellContext, IScreen?> make) => (cmd, ctx) => Task.FromResult(make(cmd, ctx));
}
