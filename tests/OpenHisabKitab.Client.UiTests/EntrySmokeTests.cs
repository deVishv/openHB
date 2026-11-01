using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>Keyboard only flows for masters, vouchers, FIND, and LOCK through the real client and host (spec 11).</summary>
[Collection("UI")]
public sealed class EntrySmokeTests
{
    static readonly string[] Codes = ["ACCT", "ITEM", "PRTY", "SALE", "PURC", "CRNT", "DBNT", "RCPT", "PYMT", "JRNL", "CNTR", "FIND", "LOCK"];

    /// <summary>Sets up the Owner and creates "A Traders" (unregistered, so no GSTIN is needed) from the keyboard, as the first run test does.</summary>
    static async Task<AppSession> StartWithCompanyAsync(string? settings = null)
    {
        var s = await AppSession.StartAsync(settings: settings);
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"), "CMPY did not open after setup.");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Keyboard.Type("A Traders");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_S);
        Keyboard.Type("27");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_R);
        Keyboard.Type("U");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppSession.WaitFor(() => s.Window.Title.StartsWith("Open Hisab Kitab: A Traders", StringComparison.Ordinal), $"Title was \"{s.Window.Title}\".");
        return s;
    }

    static void Run(AppSession s, string command)
    {
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
        Keyboard.Type(command);
        Keyboard.Press(VirtualKeyShort.ENTER);
        var code = command.Split(' ')[0].ToUpperInvariant();
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains(" " + code, StringComparison.Ordinal)), $"{code} did not open. Tabs: " + string.Join(", ", s.TabTitles()));
        Thread.Sleep(300);
    }

    static void Type(string text)
    {
        Keyboard.Type(text);
        Thread.Sleep(100);
    }

    static void Press(VirtualKeyShort key)
    {
        Keyboard.Press(key);
        Thread.Sleep(150);
    }

    static bool Shows(AppSession s, string text) => s.StatusTexts().Any(t => t.Contains(text, StringComparison.Ordinal));

    [UiFact]
    public async Task Every_new_screen_opens_named_with_distinct_access_keys()
    {
        await using var s = await StartWithCompanyAsync();
        foreach (var code in Codes)
        {
            Run(s, code.ToLowerInvariant());
            var focusable = s.Window.FindAllDescendants().Where(e => !e.IsOffscreen && e.ControlType != ControlType.TitleBar && e.Properties.IsKeyboardFocusable.ValueOrDefault).ToList();
            var unnamed = focusable.Where(e => string.IsNullOrWhiteSpace(e.Name)).Select(e => $"{e.ControlType} {e.AutomationId}").ToList();
            Assert.True(unnamed.Count == 0, $"{code}: focusable elements with no name: " + string.Join(", ", unnamed));
            // Menu access keys belong to the menu bar; the screen's own must not share a letter, or Alt cycles instead of jumping.
            var keys = s.Window.FindAllDescendants()
                .Where(e => !e.IsOffscreen && e.ControlType is not (ControlType.MenuItem or ControlType.Menu or ControlType.MenuBar))
                .Select(e => (e.Properties.AccessKey.ValueOrDefault ?? "").ToUpperInvariant()).Where(k => k.Length > 0).ToList();
            var clashes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(clashes.Count == 0, $"{code}: access keys used twice: " + string.Join(", ", clashes));
        }
    }

    [UiFact]
    public async Task Acct_adds_a_ledger_with_an_opening()
    {
        await using var s = await StartWithCompanyAsync();
        Run(s, "acct");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_N);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Type("Petty Cash");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_U);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type("cash in hand");
        Press(VirtualKeyShort.TAB);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_O);
        Type("500");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppSession.WaitFor(() => Shows(s, "Opening difference 500.00 Dr"), "No opening difference. Visible text: " + string.Join(" | ", s.StatusTexts().Where(t => t.Length > 0)));
        Assert.True(Shows(s, "Petty Cash"));
    }

    [UiFact]
    public async Task Receipt_and_find_and_lock_from_the_keyboard()
    {
        await using var s = await StartWithCompanyAsync();
        var year = Core.Time.FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today));
        var date = year.Start.AddDays(10).ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

        Run(s, "rcpt");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type(date);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_I);
        Type("cash");
        Press(VirtualKeyShort.TAB);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Type("counter sale");
        Press(VirtualKeyShort.TAB);
        Type("sales");
        Press(VirtualKeyShort.TAB);
        Type("750");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppSession.WaitFor(() => Shows(s, "Saved RCT/"), "The receipt did not save. Visible text: " + string.Join(" | ", s.StatusTexts().Where(t => t.Length > 0)));

        Run(s, "find counter");
        AppSession.WaitFor(() => Shows(s, "counter sale"), "FIND did not list the receipt.");
        Thread.Sleep(300);
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains("RCPT RCT/", StringComparison.Ordinal)), "Enter did not open the receipt. Tabs: " + string.Join(", ", s.TabTitles()));

        Run(s, "lock " + year.Start.AddDays(20).ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture));
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppSession.WaitFor(() => Shows(s, "Locked up to"), "LOCK did not lock.");

        Run(s, "find counter");
        AppSession.WaitFor(() => Shows(s, "counter sale"));
        Thread.Sleep(300);
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains("RCPT RCT/", StringComparison.Ordinal)));
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Type(" edited");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        AppSession.WaitFor(() => Shows(s, "Books are locked up to"), "The locked edit was not refused. Visible text: " + string.Join(" | ", s.StatusTexts().Where(t => t.Length > 0)));
    }

    [UiFact]
    public async Task Entry_grid_fits_at_28_px()
    {
        await using var s = await StartWithCompanyAsync(settings: """{ "ui.fontSize": 28 }""");
        s.Window.Patterns.Transform.Pattern.Resize(1024, 768);
        Run(s, "sale");
        Thread.Sleep(500);
        var area = s.Window.BoundingRectangle;
        foreach (var name in new[] { "Party", "Item", "Price", "Save" })
        {
            var e = s.Window.FindFirstDescendant(cf => cf.ByName(name))
                ?? throw new InvalidOperationException($"No element named {name}.");
            var r = e.BoundingRectangle;
            Assert.True(!r.IsEmpty && area.Contains(r), $"{name} at {r} is outside the window {area}.");
        }
    }
}
