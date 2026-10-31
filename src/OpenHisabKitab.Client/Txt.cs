using System.Windows.Markup;
using OpenHisabKitab.Client.Model.Resources;

namespace OpenHisabKitab.Client;

/// <summary>{t:Txt Key} in XAML: screen text from Client.Model's Text.resx. A missing key throws, so a typo fails a test instead of showing the key.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TxtExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        Text.ResourceManager.GetString(key, Text.Culture) ?? throw new KeyNotFoundException($"No text named {key}.");
}

/// <summary>{t:Name Key}: the same text for a UI Automation name, without the access key underscore ("_Host" is read as "Host").</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class NameExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        ((string)new TxtExtension(key).ProvideValue(serviceProvider)).Replace("__", "\u0001", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal).Replace("\u0001", "_", StringComparison.Ordinal);
}
