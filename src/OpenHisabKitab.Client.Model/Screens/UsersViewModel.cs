using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenHisabKitab.Client.Model.Formatting;
using OpenHisabKitab.Client.Model.Resources;
using OpenHisabKitab.Client.Model.Shell;
using OpenHisabKitab.Contracts.V1;
using OpenHisabKitab.Core.Commands;

namespace OpenHisabKitab.Client.Model.Screens;

/// <summary>One of the user's API tokens. The token itself is never kept in a row.</summary>
public sealed record TokenRow(int Id, string Name, string Access, string Created, string LastUsed, string Revoked);

public sealed record UserRow(int Id, string Name, string Role, bool Active, IReadOnlyList<int> Companies);

/// <summary>A company the selected user may open, ticked or not.</summary>
public sealed partial class CompanyChoice(int id, string name, bool isChecked) : ObservableObject
{
    public int Id { get; } = id;
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool Checked { get; set; } = isChecked;
}

/// <summary>
/// USER: the user's own password and API tokens (spec 4.1, Authentication), and for the Owner the users and the companies
/// each can open (spec 8). A new token is shown once and never kept.
/// </summary>
public sealed partial class UsersViewModel : ScreenViewModel
{
    IReadOnlyList<CompanyEntry> companies = [];

    UsersViewModel(ShellContext ctx) : base(ctx) { }

    public static IScreen? Create(ParsedCommand cmd, ShellContext ctx) => new UsersViewModel(ctx);

    public override string Code => "USER";

    public bool CanManageUsers => Ctx.Has("ManageUsers");

    public IReadOnlyList<string> Roles { get; } = ["Owner", "Accountant", "DataEntry", "Viewer"];

    [ObservableProperty]
    public partial IReadOnlyList<TokenRow> Tokens { get; private set; } = [];

    [ObservableProperty]
    public partial string NewTokenName { get; set; } = "";

    /// <summary>Read only unless chosen otherwise (spec 4.1).</summary>
    [ObservableProperty]
    public partial bool NewTokenReadWrite { get; set; }

    /// <summary>The token just created, shown once. Null after any reload.</summary>
    [ObservableProperty]
    public partial string? CreatedToken { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<UserRow> Users { get; private set; } = [];

    [ObservableProperty]
    public partial string NewUserName { get; set; } = "";

    [ObservableProperty]
    public partial string NewUserRole { get; set; } = "Viewer";

    [ObservableProperty]
    public partial UserRow? SelectedUser { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<CompanyChoice> CompanyChoices { get; private set; } = [];

    /// <summary>Three calls: the tokens, then for the Owner the users and the companies.</summary>
    public override Task LoadAsync(CancellationToken ct)
    {
        CreatedToken = null;
        return RunAsync(async () =>
        {
            await LoadTokensAsync(ct);
            if (!CanManageUsers) return;
            await LoadUsersAsync(ct);
            companies = [.. (await Ctx.Host.CallAsync((h, o) => h.Companies.ListAsync(new Empty(), o), ct)).Companies];
            ShowChoices();
        });
    }

    public async Task ChangePasswordAsync(string current, string next, string confirm)
    {
        if (next != confirm)
        {
            Message = Text.NewPasswordsDiffer;
            return;
        }
        if (await RunAsync(() => Ctx.Host.CallAsync((h, o) => h.Session.ChangePasswordAsync(new ChangePasswordRequest { Current = current, Next = next }, o))))
            Message = Text.PasswordChanged;
    }

    public async Task CreateTokenAsync()
    {
        var request = new CreateTokenRequest { Name = NewTokenName.Trim(), ReadWrite = NewTokenReadWrite };
        CreatedTokenMsg? created = null;
        CreatedToken = null;
        if (!await RunAsync(async () =>
            {
                created = await Ctx.Host.CallAsync((h, o) => h.Tokens.CreateAsync(request, o));
                await LoadTokensAsync(CancellationToken.None);
            })) return;
        CreatedToken = created!.Token;
        (NewTokenName, NewTokenReadWrite) = ("", false);
        Message = Text.TokenShownOnce;
    }

    public async Task RevokeAsync(TokenRow row)
    {
        if (!await Ctx.Confirm.AskAsync(string.Format(CultureInfo.InvariantCulture, Text.RevokeQuestion, row.Name))) return;
        await RunAsync(async () =>
        {
            await Ctx.Host.CallAsync((h, o) => h.Tokens.RevokeAsync(new TokenRef { Id = row.Id }, o));
            await LoadTokensAsync(CancellationToken.None);
        });
    }

    /// <summary>The password is a parameter, never a property, so it is not kept.</summary>
    public async Task CreateUserAsync(string password)
    {
        var request = new CreateUserRequest { Name = NewUserName.Trim(), Password = password, Role = NewUserRole };
        if (!await RunAsync(async () =>
            {
                await Ctx.Host.CallAsync((h, o) => h.Admin.CreateUserAsync(request, o));
                await LoadUsersAsync(CancellationToken.None);
            })) return;
        NewUserName = "";
        Message = Text.UserCreated;
    }

    public async Task SaveCompaniesAsync()
    {
        if (SelectedUser is not { } user) return;
        var request = new SetUserCompaniesRequest { UserId = user.Id, Companies = { CompanyChoices.Where(c => c.Checked).Select(c => c.Id) } };
        if (await RunAsync(async () =>
            {
                await Ctx.Host.CallAsync((h, o) => h.Admin.SetUserCompaniesAsync(request, o));
                await LoadUsersAsync(CancellationToken.None);
            }))
            Message = Text.Saved;
    }

    partial void OnSelectedUserChanged(UserRow? value) => ShowChoices();

    void ShowChoices() =>
        CompanyChoices = SelectedUser is { } user ? [.. companies.Select(c => new CompanyChoice(c.Id, c.Name, user.Companies.Contains(c.Id)))] : [];

    async Task LoadTokensAsync(CancellationToken ct)
    {
        var list = await Ctx.Host.CallAsync((h, o) => h.Tokens.ListAsync(new Empty(), o), ct);
        Tokens = [.. list.Tokens.Select(t => new TokenRow(t.Id, t.Name, t.ReadWrite ? Text.ReadWriteAccess : Text.ReadOnlyAccess,
            Day(t.CreatedAt), t.LastUsedAt.Length == 0 ? Text.Never : Day(t.LastUsedAt), t.RevokedAt.Length == 0 ? "" : Day(t.RevokedAt)))];
    }

    async Task LoadUsersAsync(CancellationToken ct)
    {
        var selected = SelectedUser?.Id;
        var list = await Ctx.Host.CallAsync((h, o) => h.Admin.ListUsersAsync(new Empty(), o), ct);
        Users = [.. list.Users.Select(u => new UserRow(u.Id, u.Name, u.Role, u.Active, [.. u.Companies]))];
        SelectedUser = Users.FirstOrDefault(u => u.Id == selected);
    }

    /// <summary>A round trip UTC timestamp as this PC's local date.</summary>
    string Day(string timestamp)
    {
        var at = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        var local = TimeZoneInfo.ConvertTime(at, Ctx.Time.LocalTimeZone);
        return DateText.Format(DateOnly.FromDateTime(local.DateTime), Ctx.Settings.Current.Text("ui.dateFormat"));
    }
}
