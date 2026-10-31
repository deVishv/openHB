namespace OpenHisabKitab.Core.Settings;

/// <summary>Settings layers (spec 7). Each overrides the ones before it.</summary>
public enum SettingLayer { Default, Machine, Company, User }

/// <summary>The layers a setting may be set in.</summary>
[Flags]
public enum SettingScope { Machine = 1, Company = 2, User = 4 }

public enum SettingType { Boolean, Integer, Text, Choice }

/// <summary>One dot named setting: its type, default, one line description, and allowed values (spec 7).</summary>
public sealed record SettingDefinition(string Key, SettingType Type, string DefaultJson, string Description,
    SettingScope Scope, int? Min = null, int? Max = null, IReadOnlyList<string>? Choices = null)
{
    public string Category => Key[..Key.IndexOf('.', StringComparison.Ordinal)];
}
