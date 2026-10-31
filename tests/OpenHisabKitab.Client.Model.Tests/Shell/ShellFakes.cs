using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;
using OpenHisabKitab.Core.Commands.Keys;

namespace OpenHisabKitab.Client.Model.Tests.Shell;

public sealed class FakeScreen(string code, bool unsaved = false, string subject = "", bool handlesCancel = false) : IScreen
{
    public string Code { get; } = code;
    public string Subject { get; } = subject;
    public bool HasUnsavedEdits { get; set; } = unsaved;
    public int Loads { get; private set; }
    public int Saves { get; private set; }
    public int Finds { get; private set; }
    public Func<ChangeEventMsg, bool> Affected { get; set; } = _ => true;

    public Task LoadAsync(CancellationToken ct)
    {
        Loads++;
        return Task.CompletedTask;
    }

    public bool IsAffectedBy(ChangeEventMsg change) => Affected(change);

    public bool Cancel() => handlesCancel;

    public Task SaveAsync()
    {
        Saves++;
        return Task.CompletedTask;
    }

    public void Find() => Finds++;
}

public sealed class FakeConfirm(bool answer) : IConfirm
{
    public int Asked { get; private set; }
    public string? LastQuestion { get; private set; }

    public Task<bool> AskAsync(string question)
    {
        Asked++;
        LastQuestion = question;
        return Task.FromResult(answer);
    }
}

public static class Keys
{
    public static readonly HashSet<string> NoContext = [];
    public static readonly HashSet<string> InPrompt = ["promptFocus"];

    public static KeyChord Chord(string text) => KeyChord.TryParse(text, out var c) ? c : throw new ArgumentException(text);

    /// <summary>Factories that open a FakeScreen for each code, keeping every screen made.</summary>
    public static Dictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> Fakes(List<FakeScreen> made, bool unsaved, params string[] codes) =>
        codes.ToDictionary(c => c, c => (Func<ParsedCommand, ShellContext, IScreen?>)((cmd, _) =>
        {
            var s = new FakeScreen(c, unsaved, string.Join(' ', cmd.Args));
            made.Add(s);
            return s;
        }));
}
