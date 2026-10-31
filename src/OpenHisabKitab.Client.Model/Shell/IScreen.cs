using OpenHisabKitab.Contracts.V1;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>What a function shows in a tab.</summary>
public interface IScreen
{
    /// <summary>The function code, such as "HELP".</summary>
    string Code { get; }

    /// <summary>A short subject for the tab title, or "".</summary>
    string Subject { get; }

    bool HasUnsavedEdits { get; }

    Task LoadAsync(CancellationToken ct);

    /// <summary>True when the change means what the screen shows may be out of date.</summary>
    bool IsAffectedBy(ChangeEventMsg change);

    /// <summary>Esc. True when the screen handled it, by closing a sub panel or clearing a field; otherwise Esc goes back.</summary>
    bool Cancel();

    /// <summary>Ctrl+S. Screens with nothing to save do nothing.</summary>
    Task SaveAsync();

    /// <summary>Ctrl+F. Focuses the screen's filter when it has one.</summary>
    void Find();
}

/// <summary>Asks one yes or no question; the view shows it as a keyboard Yes/No box.</summary>
public interface IConfirm
{
    Task<bool> AskAsync(string question);
}
