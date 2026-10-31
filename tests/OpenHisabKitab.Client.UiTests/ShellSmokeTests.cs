using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using OpenHisabKitab.Core.Time;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>Keyboard only flows through the real client and host (spec 11, UI smoke tests).</summary>
[Collection("UI")]   // one client window at a time: keystrokes go to the focused window
public sealed class ShellSmokeTests
{
    [UiFact]
    public async Task First_run_connects_trusts_sets_up_and_creates_a_company()
    {
        await using var s = await AppSession.StartAsync(trusted: false);
        s.Find("Host", ControlType.Edit);
        Keyboard.Type("localhost");
        Keyboard.Press(VirtualKeyShort.TAB);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Keyboard.Press(VirtualKeyShort.ENTER);
        s.Find("Trust", ControlType.Button);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_T);
        s.SetUpOwner();

        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"), "CMPY did not open after setup.");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Keyboard.Type("A Traders");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_S);
        Keyboard.Type("27");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_R);
        Keyboard.Type("U");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);

        var year = FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today)).Label;
        AppSession.WaitFor(() => s.Window.Title == $"Open Hisab Kitab: A Traders FY {year}", $"Title was \"{s.Window.Title}\".");
    }

    [UiFact]
    public async Task Unknown_code_shows_one_status_line()
    {
        await using var s = await AppSession.StartAsync();
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"));
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        AppSession.WaitFor(() => s.TabTitles().Count == 0);
        Keyboard.Type("xyzq");   // typed with no field focused: it goes to the prompt (spec 6.2)
        Keyboard.Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.StatusTexts().Contains("Unknown function: XYZQ"));
        Assert.Single(s.TopWindows());
    }

    [UiFact]
    public async Task F1_opens_help_and_ctrl_w_closes_it()
    {
        await using var s = await AppSession.StartAsync();
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"));
        Keyboard.Press(VirtualKeyShort.F1);
        AppSession.WaitFor(() => s.TabTitles().SequenceEqual(["1 HELP"]));
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_W);
        AppSession.WaitFor(() => s.TabTitles().Count == 0);
    }

    [UiFact]
    public async Task Menu_reaches_keys_after_ctrl_l_is_removed()
    {
        await using var s = await AppSession.StartAsync(keybindings: """[ {"key":"ctrl+l","command":"-prompt.focus"} ]""");
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"), "Tabs: " + string.Join(", ", s.TabTitles()));
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_H);
        Keyboard.Press(VirtualKeyShort.KEY_K);
        AppSession.WaitFor(() => s.TabTitles().SequenceEqual(["1 KEYS"]), "KEYS did not open from the Help menu. Tabs: " + string.Join(", ", s.TabTitles()));
    }

    [UiFact]
    public async Task Every_built_screen_opens()
    {
        await using var s = await AppSession.StartAsync();
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"));
        foreach (var code in new[] { "HELP", "DATE", "SETG", "KEYS", "USER", "CMPY" })
        {
            s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_L);
            Keyboard.Type(code.ToLowerInvariant());
            Keyboard.Press(VirtualKeyShort.ENTER);
            AppSession.WaitFor(() => !s.HasExited && s.TabTitles().Any(t => t.StartsWith("1 " + code, StringComparison.Ordinal)),
                $"{code} did not open." + (s.HasExited ? " The client closed." : " Tabs: " + string.Join(", ", s.TabTitles())));
            AssertNamed(s.Window);
        }
    }

    [UiFact]
    public async Task Every_focusable_element_has_a_name()
    {
        await using var s = await AppSession.StartAsync();
        AssertNamed(s.Window);   // the setup panel over the shell
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"));
        AssertNamed(s.Window);   // the shell with CMPY open
    }

    [UiFact]
    public async Task Largest_font_fits_1024_by_768()
    {
        await using var s = await AppSession.StartAsync(settings: """{ "ui.fontSize": 28 }""");
        s.SetUpOwner();
        s.Window.Patterns.Transform.Pattern.Resize(1024, 768);
        Thread.Sleep(500);
        var area = s.Window.BoundingRectangle;
        var parts = new List<AutomationElement>
        {
            s.Find("Command prompt", ControlType.Edit),
            s.Window.FindFirstDescendant(cf => cf.ByName("Tabs").And(cf.ByControlType(ControlType.List)))!,
            s.Window.FindFirstDescendant(cf => cf.ByName("Status").And(cf.ByControlType(ControlType.StatusBar)))!,
        };
        var fkeys = s.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .Where(b => System.Text.RegularExpressions.Regex.IsMatch(b.Name ?? "", @"^F\d{1,2}( |$)")).ToList();
        Assert.Equal(12, fkeys.Count);
        foreach (var e in parts.Concat(fkeys))
        {
            var r = e.BoundingRectangle;
            Assert.True(!r.IsEmpty && area.Contains(r), $"{e.Name} at {r} is outside the window {area}.");
        }
        for (var i = 0; i < fkeys.Count; i++)
            for (var j = i + 1; j < fkeys.Count; j++)
                Assert.False(fkeys[i].BoundingRectangle.IntersectsWith(fkeys[j].BoundingRectangle) &&
                    System.Drawing.Rectangle.Intersect(fkeys[i].BoundingRectangle, fkeys[j].BoundingRectangle) is { Width: > 1, Height: > 1 },
                    $"{fkeys[i].Name} overlaps {fkeys[j].Name}.");
    }

    static void AssertNamed(Window window)
    {
        var unnamed = window.FindAllDescendants()
            // The title bar is the system's window frame, not part of the app.
            .Where(e => !e.IsOffscreen && e.ControlType != ControlType.TitleBar && e.Properties.IsKeyboardFocusable.ValueOrDefault && string.IsNullOrWhiteSpace(e.Name))
            .Select(e => $"{e.ControlType} {e.AutomationId}").ToList();
        Assert.True(unnamed.Count == 0, "Focusable elements with no name: " + string.Join(", ", unnamed));
    }
}
