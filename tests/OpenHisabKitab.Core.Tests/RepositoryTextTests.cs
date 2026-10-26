namespace OpenHisabKitab.Core.Tests;

public class RepositoryTextTests
{
    [Fact]
    public void FindForbiddenDashes_reports_line_and_column()
    {
        var text = "ok\nbad \u2014 here\nalso \u2013";
        Assert.Equal(new[] { (2, 5), (3, 6) }, RepositoryText.FindForbiddenDashes(text));
    }

    [Fact]
    public void FindForbiddenDashes_allows_ascii_hyphen() =>
        Assert.Empty(RepositoryText.FindForbiddenDashes("INV/26-27/00001"));

    [Fact]
    public void Repository_contains_no_en_or_em_dashes()
    {
        var offenders = RepositoryText.ScanRepository();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
    }
}

public static class RepositoryText
{
    const char EnDash = (char)0x2013;
    const char EmDash = (char)0x2014;

    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", ".vs", ".superpowers", ".remember",
    };

    static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".ico", ".dll", ".exe", ".pdb", ".zip", ".snk",
    };

    public static IReadOnlyList<(int Line, int Column)> FindForbiddenDashes(string text)
    {
        var found = new List<(int, int)>();
        int line = 1, column = 1;
        foreach (var c in text)
        {
            if (c == '\n') { line++; column = 1; continue; }
            if (c == EnDash || c == EmDash) found.Add((line, column));
            column++;
        }
        return found;
    }

    public static IReadOnlyList<string> ScanRepository()
    {
        var root = FindRoot();
        var offenders = new List<string>();
        foreach (var file in EnumerateFiles(root))
        {
            foreach (var (line, column) in FindForbiddenDashes(File.ReadAllText(file)))
                offenders.Add($"{Path.GetRelativePath(root, file)}:{line}:{column}");
        }
        return offenders;
    }

    static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenHisabKitab.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    static IEnumerable<string> EnumerateFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
            if (!BinaryExtensions.Contains(Path.GetExtension(file))) yield return file;
        foreach (var sub in Directory.EnumerateDirectories(folder))
            if (!SkippedFolders.Contains(Path.GetFileName(sub)))
                foreach (var file in EnumerateFiles(sub)) yield return file;
    }
}
