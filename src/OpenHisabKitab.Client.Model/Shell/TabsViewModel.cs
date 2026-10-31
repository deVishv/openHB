using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Resources;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>
/// The tab strip (spec 6.1). Tabs are numbered 1 to n in strip order. Back and forward re-run a tab's earlier commands
/// into new screens, so a tab holds only the screen it shows.
/// </summary>
public sealed partial class TabsViewModel(Func<string, Task<IScreen?>> recreate, Func<IScreen, Task> load, IConfirm confirm) : ObservableObject
{
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    public partial TabViewModel? Active { get; set; }

    public void Show(IScreen screen, string command, bool newTab)
    {
        if (newTab || Active is null)
        {
            var tab = new TabViewModel(screen, command);
            Tabs.Add(tab);
            Renumber();
            Active = tab;
        }
        else Active.Replace(screen, command);
    }

    /// <summary>False when there is no tab, or the user keeps unsaved edits.</summary>
    public async Task<bool> CloseActiveAsync(IConfirm confirm)
    {
        if (Active is not { } tab) return false;
        if (tab.Screen.HasUnsavedEdits && !await confirm.AskAsync(Text.DiscardChanges)) return false;
        var index = Tabs.IndexOf(tab);
        Tabs.RemoveAt(index);
        Renumber();
        Active = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        return true;
    }

    /// <summary>Closes every tab without asking; the caller has asked about unsaved edits.</summary>
    public void CloseAll()
    {
        Active = null;
        Tabs.Clear();
    }

    public void Next() => Move(+1);

    public void Previous() => Move(-1);

    /// <summary>A number beyond the last tab does nothing.</summary>
    public void GoTo(int number)
    {
        if (number >= 1 && number <= Tabs.Count) Active = Tabs[number - 1];
    }

    public Task BackAsync() => StepAsync(t => t.PopBack(), (t, s, c) => t.ShowFromBack(s, c));

    public Task ForwardAsync() => StepAsync(t => t.PopForward(), (t, s, c) => t.ShowFromForward(s, c));

    async Task StepAsync(Func<TabViewModel, string?> pop, Action<TabViewModel, IScreen, string> show)
    {
        if (Active is not { } tab) return;
        if (!(tab.Screen.HasUnsavedEdits ? await confirm.AskAsync(Text.DiscardChanges) : true)) return;
        if (pop(tab) is not { } command || await recreate(command) is not { } screen) return;
        show(tab, screen, command);
        await load(screen);
    }

    void Move(int step)
    {
        if (Active is null || Tabs.Count == 0) return;
        Active = Tabs[(Tabs.IndexOf(Active) + step + Tabs.Count) % Tabs.Count];
    }

    void Renumber()
    {
        for (var i = 0; i < Tabs.Count; i++) Tabs[i].Number = i + 1;
    }
}
