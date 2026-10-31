using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>The command prompt and its autocomplete of function codes (spec 6.2, Autocomplete).</summary>
public sealed partial class PromptViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Matching functions while the first word is being typed; empty once a space is typed.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FunctionInfo> Suggestions { get; private set; } = [];

    /// <summary>The highlighted suggestion, or -1.</summary>
    [ObservableProperty]
    public partial int SelectedSuggestion { get; set; } = -1;

    partial void OnTextChanged(string value)
    {
        Suggestions = value.Trim().Length == 0 || value.Contains(' ', StringComparison.Ordinal) ? [] : FunctionCatalog.Search(value);
        SelectedSuggestion = -1;
    }

    /// <summary>Puts the highlighted code and a space in the prompt, ready for arguments.</summary>
    public void Complete()
    {
        if (SelectedSuggestion < 0 || SelectedSuggestion >= Suggestions.Count) return;
        Text = Suggestions[SelectedSuggestion].Code + " ";
    }
}
