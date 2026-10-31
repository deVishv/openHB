using System.IO;
using System.Windows;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Settings;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Client.Model.Theme;
using OpenHisabKitab.Client.Theme;
using OpenHisabKitab.Client.Views;
using OpenHisabKitab.Contracts;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client;

/// <summary>Builds the client: this PC's files, the host connection, the stores, the shell, and the window.</summary>
public partial class App : Application
{
    HostConnection? connection;
    ShellViewModel? shell;

    /// <summary>Every function with a screen. Codes not here say "{CODE} is not in this version yet.".</summary>
    static Dictionary<string, Func<ParsedCommand, ShellContext, IScreen?>> Screens() => [];

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = Profile(e.Args) is { } folder ? new ClientPaths(folder) : ClientPaths.Default();
        Directory.CreateDirectory(paths.Root);
        var files = new MachineFiles(paths);
        connection = new HostConnection(new TlsChannelFactory(), new KnownHosts(paths.KnownHosts), TimeProvider.System,
            ProductVersion.Current, Environment.MachineName);
        var ctx = new ShellContext
        {
            Host = connection, Files = files, Settings = new SettingsStore(files, connection), Keys = new KeybindingStore(files, connection),
            Time = TimeProvider.System, WorkingDate = DateOnly.FromDateTime(DateTime.Now),
        };
        await ctx.Settings.LoadAsync(null);
        await ctx.Keys.LoadAsync();
        ApplyTheme(ctx);
        ctx.Settings.Changed += () => Dispatcher.BeginInvoke(() => ApplyTheme(ctx));
        SystemParameters.StaticPropertyChanged += (_, p) =>
        {
            if (p.PropertyName == nameof(SystemParameters.HighContrast)) ApplyTheme(ctx);
        };

        var window = new MainWindow(files);
        shell = new ShellViewModel(ctx, new FunctionRouter(Screens()), new DialogConfirm(window));
        window.Attach(shell);
        MainWindow = window;
        window.Show();
        shell.Start();
        if (ctx.Settings.LoadErrors.Concat(ctx.Keys.LoadErrors).FirstOrDefault() is { } error) shell.Status.Message = error;
        // A host typed before is tried at once; the connect panel shows if it does not answer.
        if (ctx.Settings.Current.Text("connection.host").Length > 0) await shell.Session.ConnectAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (shell is not null) await shell.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        base.OnExit(e);
    }

    void ApplyTheme(ShellContext ctx) => ThemeResources.Apply(Resources, ThemeColors.Classic, ctx.Settings.Current, SystemParameters.HighContrast);

    /// <summary>--profile &lt;folder&gt; keeps this PC's files in another folder, for a second client or for tests.</summary>
    static string? Profile(string[] args)
    {
        var i = Array.IndexOf(args, "--profile");
        return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : null;
    }
}
