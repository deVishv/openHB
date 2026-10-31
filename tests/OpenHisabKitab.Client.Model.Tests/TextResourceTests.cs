using System.Text.RegularExpressions;
using OpenHisabKitab.Client.Model.Resources;

namespace OpenHisabKitab.Client.Model.Tests;

/// <summary>Spec 2: all screen text lives in resource files. XAML reaches it only through {t:Txt Key}.</summary>
public sealed partial class TextResourceTests
{
    static IEnumerable<string> XamlFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenHisabKitab.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var client = Path.Combine(dir.FullName, "src", "OpenHisabKitab.Client");
        var files = Directory.EnumerateFiles(client, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);
        return files;
    }

    [GeneratedRegex(@"\{t:Txt\s+(\w+)\s*\}")]
    private static partial Regex TxtKey();

    [GeneratedRegex(@"\s(Text|Content|Header|Title|ToolTip|AutomationProperties\.Name)=""([^""]*)""")]
    private static partial Regex TextAttribute();

    [Fact]
    public void Every_txt_key_exists()
    {
        foreach (var file in XamlFiles())
            foreach (Match m in TxtKey().Matches(File.ReadAllText(file)))
                Assert.True(Text.ResourceManager.GetString(m.Groups[1].Value, CultureInfo.InvariantCulture) is not null,
                    $"{Path.GetFileName(file)}: no text named {m.Groups[1].Value}");
    }

    [Fact]
    public void No_literal_text_in_xaml()
    {
        foreach (var file in XamlFiles())
            foreach (Match m in TextAttribute().Matches(File.ReadAllText(file)))
            {
                var value = m.Groups[2].Value;
                Assert.False(value.Any(char.IsLetter) && !value.StartsWith('{'),
                    $"{Path.GetFileName(file)}: {m.Groups[1].Value}=\"{value}\" is literal text");
            }
    }
}
