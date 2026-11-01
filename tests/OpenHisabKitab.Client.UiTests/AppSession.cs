using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using OpenHisabKitab.Host;

namespace OpenHisabKitab.Client.UiTests;

/// <summary>
/// A real host in this process on a port the OS picks, and the real client exe with its own profile folder, driven
/// through UI Automation with the keyboard only (spec 2, anti-goals).
/// </summary>
public sealed class AppSession : IAsyncDisposable
{
    public const string Password = "owner-password-1";
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    readonly WebApplication host;
    readonly UIA3Automation automation = new();
    Application? app;

    AppSession(WebApplication host, string hostFolder, string profile, int port)
    {
        this.host = host;
        HostFolder = hostFolder;
        Profile = profile;
        Port = port;
    }

    public string HostFolder { get; }
    public string Profile { get; }
    public int Port { get; }
    public Window Window { get; private set; } = null!;

    public bool HasExited => app?.HasExited ?? true;

    /// <summary>
    /// Starts the host. With trusted set, this PC already trusts the host and knows its address, so the client goes
    /// straight to Owner setup. Settings and keybindings, when given, are this PC's files.
    /// </summary>
    public static async Task<AppSession> StartAsync(bool trusted = true, string? settings = null, string? keybindings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "ohk-ui-tests", Guid.NewGuid().ToString("N"));
        var hostFolder = Path.Combine(root, "host");
        var profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(profile);
        var host = HostApp.Build(["--Host:DataDirectory=" + hostFolder, "--Host:GrpcPort=0"]);
        await host.StartAsync();
        var address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var port = new Uri(address.Replace("[::]", "localhost", StringComparison.Ordinal)).Port;
        var session = new AppSession(host, hostFolder, profile, port);
        if (trusted)
        {
            var fingerprint = File.ReadAllText(Path.Combine(hostFolder, "host-cert-fingerprint.txt")).Trim();
            File.WriteAllText(Path.Combine(profile, "known-hosts.json"), JsonSerializer.Serialize(new Dictionary<string, string> { [$"localhost:{port}"] = fingerprint }));
            var values = settings is null ? "" : settings.Trim().TrimStart('{').TrimEnd('}').Trim();
            File.WriteAllText(Path.Combine(profile, "settings.json"),
                $"{{ \"connection.host\": \"localhost\", \"connection.port\": {port}{(values.Length > 0 ? ", " + values : "")} }}");
        }
        else if (settings is not null) File.WriteAllText(Path.Combine(profile, "settings.json"), settings);
        if (keybindings is not null) File.WriteAllText(Path.Combine(profile, "keybindings.json"), keybindings);
        session.Launch();
        return session;
    }

    void Launch()
    {
        app = Application.Launch(new ProcessStartInfo(ClientExe(), $"--profile \"{Profile}\"") { UseShellExecute = false });
        Window = app.GetMainWindow(automation, Wait) ?? throw new InvalidOperationException("The client window did not open.");
        // Every test types from here on, so the client must have the keyboard before anything else happens.
        BringToFront();
    }

    /// <summary>Owner setup through the setup panel, typed like a user would.</summary>
    public void SetUpOwner()
    {
        Find("Set up the Owner.", ControlType.Text);
        // The panel focuses its first field; a window that opened behind another must first come to the front.
        BringToFront();
        var user = Find("User", ControlType.Edit);
        if (!user.Properties.HasKeyboardFocus.ValueOrDefault) user.Focus();
        WaitFor(() => user.Properties.HasKeyboardFocus.ValueOrDefault, "The User field did not take focus.");
        // Input sent the instant a window comes to the front can be dropped, so each field gets a moment.
        Thread.Sleep(300);
        // Keystrokes go to whichever window is in front: never type unless it is this client.
        BringToFront();
        foreach (var (text, next) in new[] { ("owner", VirtualKeyShort.TAB), (Password, VirtualKeyShort.TAB), (Password, VirtualKeyShort.ENTER) })
        {
            Keyboard.Type(text);
            Thread.Sleep(100);
            Keyboard.Press(next);
            Thread.Sleep(100);
        }
        if (!Retry.WhileFalse(() => StatusTexts().Any(t => t.StartsWith("Connected to", StringComparison.Ordinal)), Wait, throwOnTimeout: false).Result)
            FlaUI.Core.Capturing.Capture.Element(Window).ToFile(Path.Combine(Path.GetTempPath(), "ohk-ui-setup-failed.png"));
        WaitFor(() => StatusTexts().Any(t => t.StartsWith("Connected to", StringComparison.Ordinal)),
            "Setup did not connect. Visible text: " + string.Join(" | ", StatusTexts().Where(t => t.Length > 0)));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool BringWindowToTop(nint window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    // Windows lets only the window that has the keyboard pass it on. Joining its input thread for a moment lets this
    // process ask for it the same way, which a test runner in the background is otherwise refused.
    void TakeForeground()
    {
        var target = Window.Properties.NativeWindowHandle.ValueOrDefault;
        var current = GetForegroundWindow();
        var mine = GetCurrentThreadId();
        var theirs = GetWindowThreadProcessId(current, out _);
        var joined = theirs != 0 && theirs != mine && AttachThreadInput(mine, theirs, true);
        try
        {
            BringWindowToTop(target);
            SetForegroundWindow(target);
        }
        finally
        {
            if (joined) AttachThreadInput(mine, theirs, false);
        }
    }

    bool InFront() => GetForegroundWindow() == Window.Properties.NativeWindowHandle.ValueOrDefault;

    /// <summary>
    /// Brings the client in front and waits until Windows really gives it the keyboard. Windows can refuse a new window the
    /// foreground, and keystrokes then go to the terminal instead; a test that cannot get the window in front fails rather than type there.
    /// </summary>
    public void BringToFront()
    {
        for (var i = 0; i < 10 && !InFront(); i++)
        {
            TakeForeground();
            Window.Focus();
            Thread.Sleep(200);
        }
        Assert.True(InFront(), "The client window could not be brought to the front, so no keys were typed. Keep other windows from taking focus during UI tests.");
    }

    public AutomationElement Find(string name, ControlType type) =>
        Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(type))), Wait).Result
        ?? throw new InvalidOperationException($"No {type} named {name}.");

    public AutomationElement? Maybe(string name, ControlType type) =>
        Window.FindFirstDescendant(cf => cf.ByName(name).And(cf.ByControlType(type)));

    public IReadOnlyList<string> StatusTexts() =>
        [.. Window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Where(e => !e.IsOffscreen).Select(e => e.Name ?? "")];

    public IReadOnlyList<string> TabTitles() =>
        Window.FindFirstDescendant(cf => cf.ByName("Tabs").And(cf.ByControlType(ControlType.List))) is { } strip
            ? [.. strip.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Select(e => e.Name ?? "")]
            : [];

    public IReadOnlyList<Window> TopWindows() => app!.GetAllTopLevelWindows(automation);

    public void Chord(params VirtualKeyShort[] keys)
    {
        Keyboard.TypeSimultaneously(keys);
        Thread.Sleep(150);
    }

    public static void WaitFor(Func<bool> condition, string? what = null)
    {
        var done = Retry.WhileFalse(condition, Wait, throwOnTimeout: false).Result;
        Assert.True(done, what ?? "The screen did not reach the expected state.");
    }

    static string ClientExe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenHisabKitab.slnx"))) dir = dir.Parent;
        var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(dir!.FullName, "src", "OpenHisabKitab.Client", "bin", config, "net10.0-windows", "OpenHisabKitab.Client.exe");
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            app.Close();
            if (!app.HasExited) app.Kill();
            app.Dispose();
        }
        automation.Dispose();
        await host.StopAsync();
        await host.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Path.GetDirectoryName(Profile)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
