using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Connection;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Core.Settings;

namespace OpenHisabKitab.Client.Model.Shell;

/// <summary>The panel over the shell while the client is not in: connect, trust, set up, log in, or install another version.</summary>
public enum SessionPanel { None, Connect, Trust, CertificateChanged, Setup, Login, VersionMismatch }

/// <summary>
/// Getting in (spec 13, First run): the host address typed as a name and port, the certificate accepted once, then the
/// Owner set up or a login. After a lost session only the login panel shows; tabs and typed text stay under it.
/// </summary>
public sealed partial class SessionViewModel : ObservableObject
{
    readonly ShellContext ctx;
    readonly Func<Task> loggedIn;
    bool changingHost;
    string? ownMessage;

    public SessionViewModel(ShellContext ctx, Func<Task> loggedIn)
    {
        this.ctx = ctx;
        this.loggedIn = loggedIn;
        HostName = ctx.Settings.Current.Text("connection.host");
        Port = ctx.Settings.Current.Int("connection.port").ToString(CultureInfo.InvariantCulture);
        var ui = SynchronizationContext.Current;
        ctx.Host.StateChanged += _ =>
        {
            // The retry loop reports from a pool thread; the panel and the user name box belong to the UI thread.
            void Show()
            {
                if (ctx.Host.Session is { } s && string.IsNullOrEmpty(UserName)) UserName = s.UserName;
                Refresh();
            }
            if (ui is null || SynchronizationContext.Current == ui) Show();
            else ui.Post(_ => Show(), null);
        };
    }

    [ObservableProperty]
    public partial string HostName { get; set; }

    [ObservableProperty]
    public partial string Port { get; set; }

    [ObservableProperty]
    public partial string UserName { get; set; } = "";

    public SessionPanel Panel => changingHost ? SessionPanel.Connect : ctx.Host.State switch
    {
        ConnectionState.Connected or ConnectionState.Reconnecting => SessionPanel.None,
        ConnectionState.NeedsTrust => SessionPanel.Trust,
        ConnectionState.CertificateChanged => SessionPanel.CertificateChanged,
        ConnectionState.VersionMismatch => SessionPanel.VersionMismatch,
        ConnectionState.NeedsSetup => SessionPanel.Setup,
        ConnectionState.NeedsLogin => SessionPanel.Login,
        _ => SessionPanel.Connect,
    };

    /// <summary>The one line for the panel: what the user typed wrong, or why the host refused.</summary>
    public string? Message => ownMessage ?? (Panel == SessionPanel.None ? null : ctx.Host.Message);

    public string? SeenFingerprint => ctx.Host.SeenFingerprint;

    public string? PinnedFingerprint => ctx.Host.PinnedFingerprint;

    public async Task ConnectAsync()
    {
        ownMessage = null;
        if (!HostAddress.TryParse(HostName, Port, out var address, out var error))
        {
            ownMessage = error;
            Refresh();
            return;
        }
        changingHost = false;
        if (ctx.Settings.Current.Text("connection.host") != address.Name)
            await ctx.Settings.SetAsync("connection.host", JsonText(address.Name), SettingLayer.Machine, null);
        if (ctx.Settings.Current.Int("connection.port") != address.Port)
            await ctx.Settings.SetAsync("connection.port", address.Port.ToString(CultureInfo.InvariantCulture), SettingLayer.Machine, null);
        await ctx.Host.ConnectAsync(address);
        Refresh();
    }

    public async Task TrustAsync()
    {
        ownMessage = null;
        await ctx.Host.TrustSeenCertificateAsync();
        Refresh();
    }

    /// <summary>The password is a parameter, never a property, so it is not kept after the call.</summary>
    public Task SetupAsync(string password, string confirm)
    {
        if (password != confirm)
        {
            ownMessage = Text.PasswordsDiffer;
            Refresh();
            return Task.CompletedTask;
        }
        return SignInAsync(() => ctx.Host.SetupOwnerAsync(UserName.Trim(), password));
    }

    public Task LoginAsync(string password) => SignInAsync(() => ctx.Host.LoginAsync(UserName.Trim(), password));

    /// <summary>Back to the connect panel, to type another host.</summary>
    public void ChangeHost()
    {
        changingHost = true;
        ownMessage = null;
        Refresh();
    }

    async Task SignInAsync(Func<Task> signIn)
    {
        ownMessage = null;
        try { await signIn(); }
        catch (HostException e)
        {
            ownMessage = e.Message;
            Refresh();
            return;
        }
        Refresh();
        await loggedIn();
    }

    void Refresh()
    {
        OnPropertyChanged(nameof(Panel));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(SeenFingerprint));
        OnPropertyChanged(nameof(PinnedFingerprint));
    }

    static string JsonText(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
