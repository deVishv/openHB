using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>Every function that has a screen in this build. A catalog code missing here says "{CODE} is not in this version yet.".</summary>
public static class ScreenCatalog
{
    public static Dictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> Built() => new()
    {
        ["HELP"] = HelpViewModel.Create,
        ["DATE"] = DateViewModel.Create,
    };
}
