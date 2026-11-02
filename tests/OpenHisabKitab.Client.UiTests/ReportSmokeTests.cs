using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>Keyboard only flows for the report screens, AUDT, and BKUP through the real client and host (spec 11).</summary>
[Collection("UI")]
public sealed class ReportSmokeTests
{
    static readonly string[] Codes = ["TRLB", "LEDG cash", "DAYB", "PNLS", "BSHT", "CFLO", "OUTS", "AUDT", "BKUP"];

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
        Thread.Sleep(400);
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

    static string Visible(AppSession s) => string.Join(" | ", s.StatusTexts().Where(t => t.Length > 0));

    /// <summary>A cash receipt "counter sale" of 750.00 against Sales, dated early in the current year, entered from the keyboard.</summary>
    static void CashSale(AppSession s)
    {
        var year = Core.Time.FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today));
        Run(s, "rcpt");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type(year.Start.AddDays(10).ToString("dd-MM-yyyy", CultureInfo.InvariantCulture));
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
        AppSession.WaitFor(() => Shows(s, "Saved RCT/"), "The receipt did not save. Visible text: " + Visible(s));
    }

    [UiFact]
    public async Task Every_report_screen_opens_named_with_distinct_access_keys()
    {
        await using var s = await StartWithCompanyAsync();
        foreach (var code in Codes)
        {
            Run(s, code.ToLowerInvariant());
            var focusable = s.Window.FindAllDescendants().Where(e => !e.IsOffscreen && e.ControlType != ControlType.TitleBar && e.Properties.IsKeyboardFocusable.ValueOrDefault).ToList();
            var unnamed = focusable.Where(e => string.IsNullOrWhiteSpace(e.Name)).Select(e => $"{e.ControlType} {e.AutomationId}").ToList();
            Assert.True(unnamed.Count == 0, $"{code}: focusable elements with no name: " + string.Join(", ", unnamed));
            var keys = s.Window.FindAllDescendants()
                .Where(e => !e.IsOffscreen && e.ControlType is not (ControlType.MenuItem or ControlType.Menu or ControlType.MenuBar))
                .Select(e => (e.Properties.AccessKey.ValueOrDefault ?? "").ToUpperInvariant()).Where(k => k.Length > 0).ToList();
            var clashes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(clashes.Count == 0, $"{code}: access keys used twice: " + string.Join(", ", clashes));
            // Alt and a menu bar letter opens the menu, so a screen key on the same letter can never be reached.
            var menu = s.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .Select(e => (e.Properties.AccessKey.ValueOrDefault ?? "").ToUpperInvariant()).Where(k => k.Length > 0).ToHashSet();
            var shadowed = keys.Where(menu.Contains).Distinct().ToList();
            Assert.True(shadowed.Count == 0, $"{code}: access keys the menu bar takes: " + string.Join(", ", shadowed));
        }
    }

    [UiFact]
    public async Task Trlb_drills_to_ledg_and_back()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "trlb");
        AppSession.WaitFor(() => Shows(s, "Trial balance,"), "TRLB showed no heading. Visible text: " + Visible(s));
        // The first line is Cash, and the report puts the cursor on it.
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains("LEDG Cash", StringComparison.Ordinal)), "Enter did not open LEDG. Tabs: " + string.Join(", ", s.TabTitles()));
        AppSession.WaitFor(() => Shows(s, "counter sale") || Shows(s, "Opening balance"), "LEDG showed no lines. Visible text: " + Visible(s));
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.LEFT);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains(" TRLB", StringComparison.Ordinal)), "Alt+Left did not return to TRLB.");
    }

    [UiFact]
    public async Task Bsht_switches_style_with_ctrl_g()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "bsht");
        AppSession.WaitFor(() => Shows(s, "Balance sheet (Schedule III)"), "BSHT did not open in Schedule III style. Visible text: " + Visible(s));
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
        AppSession.WaitFor(() => Shows(s, "Balance sheet (chart of accounts)"), "Ctrl+G did not switch style. Visible text: " + Visible(s));
    }

    [UiFact]
    public async Task Outs_and_cflo_open_from_the_keyboard()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "outs payable");
        AppSession.WaitFor(() => Shows(s, "Payables as at"), "OUTS payable did not open. Visible text: " + Visible(s));
        Run(s, "cflo");
        AppSession.WaitFor(() => Shows(s, "Cash flow,"), "CFLO did not open. Visible text: " + Visible(s));
    }

    [UiFact]
    public async Task Daybook_opens_a_voucher()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "dayb fy");
        AppSession.WaitFor(() => Shows(s, "counter sale"), "DAYB did not list the receipt. Visible text: " + Visible(s));
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains("RCPT RCT/", StringComparison.Ordinal)), "Enter did not open the receipt. Tabs: " + string.Join(", ", s.TabTitles()));
    }

    [UiFact]
    public async Task Export_writes_a_csv()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "trlb");
        AppSession.WaitFor(() => Shows(s, "Trial balance,"));
        var file = Path.Combine(Path.GetDirectoryName(s.Profile)!, "trial-balance.csv");
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_E);
        AppSession.WaitFor(() => s.Window.ModalWindows.Length > 0, "Ctrl+E did not open the save dialog.");
        Thread.Sleep(500);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type(file);
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => File.Exists(file), "No CSV was written.");
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("Cash,", System.Text.Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [UiFact]
    public async Task Audt_verify_reports_intact()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "audt");
        AppSession.WaitFor(() => Shows(s, "Audit trail"), "AUDT did not open. Visible text: " + Visible(s));
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_R);
        AppSession.WaitFor(() => Shows(s, "Chain intact:"), "Verify did not report. Visible text: " + Visible(s));
    }

    [UiFact]
    public async Task Bkup_backs_up_now()
    {
        await using var s = await StartWithCompanyAsync();
        Run(s, "bkup");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_B);
        AppSession.WaitFor(() => Shows(s, "Backed up to"), "Back up now did not report. Visible text: " + Visible(s));
    }

    [UiFact]
    public async Task Report_fits_at_28_px()
    {
        await using var s = await StartWithCompanyAsync(settings: """{ "ui.fontSize": 28 }""");
        s.Window.Patterns.Transform.Pattern.Resize(1024, 768);
        CashSale(s);
        Run(s, "trlb");
        AppSession.WaitFor(() => Shows(s, "Trial balance,"));
        var area = s.Window.BoundingRectangle;
        var heading = s.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .FirstOrDefault(e => (e.Name ?? "").StartsWith("Trial balance,", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No heading.");
        var header = s.Window.FindFirstDescendant(cf => cf.ByName("Ledger").And(cf.ByControlType(ControlType.HeaderItem)))
            ?? throw new InvalidOperationException("No Ledger column header.");
        // A ListView with a GridView is a data grid to UI Automation, and its lines are data items.
        var lines = s.Find("Report lines", ControlType.DataGrid);
        var first = lines.FindFirstChild(cf => cf.ByControlType(ControlType.DataItem)) ?? throw new InvalidOperationException("No first line.");
        foreach (var (name, e) in new (string, AutomationElement)[] { ("heading", heading), ("Ledger header", header), ("first line", first) })
        {
            var r = e.BoundingRectangle;
            Assert.True(!r.IsEmpty && area.Contains(r), $"{name} at {r} is outside the window {area}.");
        }
    }
}
