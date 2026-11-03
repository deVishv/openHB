using System.Globalization;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>Keyboard flows for PRNT, PDF, Ship to, and the company logo through the real client and host (Plan 6b).</summary>
[Collection("UI")]
public sealed class PrintSmokeTests
{
    static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    static async Task<AppSession> StartWithCompanyAsync()
    {
        var s = await AppSession.StartAsync();
        s.SetUpOwner();
        AppSession.WaitFor(() => s.TabTitles().Contains("1 CMPY"), "CMPY did not open after setup.");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        Keyboard.Type("A Traders");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_S);
        Keyboard.Type("27");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_D);
        Keyboard.Type("1 MG Road, Pune");
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

    /// <summary>A UI Automation check that counts a timeout as "not yet": the print dialog keeps UIA busy while it lists printers.</summary>
    static Func<bool> Safe(Func<bool> check) => () =>
    {
        try { return check(); }
        catch (System.Runtime.InteropServices.COMException) { return false; }
        catch (TimeoutException) { return false; }
    };

    static bool Shows(AppSession s, string text) => s.StatusTexts().Any(t => t.Contains(text, StringComparison.Ordinal));

    static string Visible(AppSession s) => string.Join(" | ", s.StatusTexts().Where(t => t.Length > 0));

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

    /// <summary>Ctrl+Shift+E, then the save dialog driven by keys; returns the bytes written.</summary>
    static byte[] SavePdf(AppSession s, string name)
    {
        var file = Path.Combine(Path.GetDirectoryName(s.Profile)!, name);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_E);
        AppSession.WaitFor(() => s.Window.ModalWindows.Length > 0, "Ctrl+Shift+E did not open the save dialog. Visible text: " + Visible(s));
        Thread.Sleep(500);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type(file);
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => File.Exists(file) && new FileInfo(file).Length > 0, "No PDF was written.");
        Thread.Sleep(300);
        AppSession.WaitFor(() => Shows(s, "Saved " + name), "No saved message. Visible text: " + Visible(s));
        return File.ReadAllBytes(file);
    }

    [UiFact]
    public async Task Receipt_saves_as_pdf_from_the_keyboard()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "dayb fy");
        AppSession.WaitFor(() => Shows(s, "counter sale"), "DAYB did not list the receipt. Visible text: " + Visible(s));
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => s.TabTitles().Any(t => t.Contains("RCPT RCT/", StringComparison.Ordinal)), "Enter did not open the receipt.");
        Thread.Sleep(400);
        var bytes = SavePdf(s, "receipt.pdf");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [UiFact]
    public async Task Trlb_saves_as_pdf()
    {
        await using var s = await StartWithCompanyAsync();
        CashSale(s);
        Run(s, "trlb");
        AppSession.WaitFor(() => Shows(s, "Trial balance,"));
        var bytes = SavePdf(s, "trial-balance.pdf");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [UiFact]
    public async Task F11_opens_the_print_dialog_and_esc_closes_it()
    {
        await using var s = await StartWithCompanyAsync();
        Run(s, "trlb");
        AppSession.WaitFor(() => Shows(s, "Trial balance,"));
        var process = s.Window.Properties.ProcessId.Value;
        var before = Dialogs.Titles();
        Press(VirtualKeyShort.F11);
        // The Windows print dialog keeps UI Automation busy, so it is found through Win32: a visible dialog of the app.
        // Windows 10 shows the classic dialog in the app; Windows 11 a modern one in another process, titled "{app} - Print".
        bool Open() => Dialogs.Any(process) || Dialogs.Titles().Except(before).Any(t => t.EndsWith("Print", StringComparison.Ordinal));
        AppSession.WaitFor(Open, "F11 did not open the print dialog.");
        Thread.Sleep(1500);
        Press(VirtualKeyShort.ESCAPE);
        AppSession.WaitFor(() => !Open(), "Esc did not close the print dialog.");
        Assert.False(Shows(s, "Sent to"));
        Assert.Contains(s.TabTitles(), t => t.Contains(" TRLB", StringComparison.Ordinal));
    }

    [UiFact]
    public async Task Ship_to_fields_are_reached_by_tab_on_a_sale()
    {
        await using var s = await StartWithCompanyAsync();
        Run(s, "sale");
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_M);
        Thread.Sleep(200);
        Assert.Equal("Ship to name", s.Window.Automation.FocusedElement().Name);
        Type("Ravi Godown");
        Press(VirtualKeyShort.TAB);
        Assert.Equal("Ship to address", s.Window.Automation.FocusedElement().Name);
        Type("Plot 9");
        Press(VirtualKeyShort.TAB);
        Assert.Equal("Ship to state", s.Window.Automation.FocusedElement().Name);
    }

    [UiFact]
    public async Task Cmpy_sets_a_logo_by_keyboard()
    {
        await using var s = await StartWithCompanyAsync();
        var logo = Path.Combine(Path.GetDirectoryName(s.Profile)!, "logo.png");
        File.WriteAllBytes(logo, TinyPng);
        Run(s, "cmpy");
        // CMPY's Edit button carries the menu letter E, so Alt+E opens the Edit menu; the button is invoked directly.
        s.Find("Edit", ControlType.Button).AsButton().Invoke();
        Thread.Sleep(400);
        AppSession.WaitFor(() => Shows(s, "No logo"), "The edit form shows no logo row. Visible text: " + Visible(s));
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_L);
        AppSession.WaitFor(() => s.Window.ModalWindows.Length > 0, "Alt+L did not open the file dialog.");
        Thread.Sleep(500);
        s.Chord(VirtualKeyShort.ALT, VirtualKeyShort.KEY_N);
        s.Chord(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Type(logo);
        Press(VirtualKeyShort.ENTER);
        AppSession.WaitFor(() => Shows(s, "Logo saved."), "The logo did not save. Visible text: " + Visible(s));
        Assert.True(Shows(s, "Logo: 1 KB"));
    }
}

/// <summary>Visible standard dialog windows (class #32770) of a process, found without UI Automation.</summary>
static class Dialogs
{
    delegate bool EnumProc(nint hwnd, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool EnumWindows(EnumProc callback, nint lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder name, int max);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int GetWindowTextW(nint hwnd, System.Text.StringBuilder text, int max);

    /// <summary>The titles of the visible top level windows of every process.</summary>
    public static List<string> Titles()
    {
        var all = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var text = new System.Text.StringBuilder(256);
            GetWindowTextW(hwnd, text, text.Capacity);
            all.Add(text.ToString());
            return true;
        }, 0);
        return all;
    }

    public static bool Any(int process)
    {
        var found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != process || !IsWindowVisible(hwnd)) return true;
            var name = new System.Text.StringBuilder(64);
            GetClassNameW(hwnd, name, name.Capacity);
            found = name.ToString() == "#32770";
            return !found;
        }, 0);
        return found;
    }
}
