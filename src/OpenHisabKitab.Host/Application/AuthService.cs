using System.Globalization;
using System.Text.Json;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>First run setup, login, logout, and password changes (spec 8, 13).</summary>
public sealed class AuthService(HostData data, SessionStore sessions, LoginThrottle throttle, TimeProvider time)
{
    public const int MinPasswordLength = 8;

    // Verified against when the user name is unknown, so a wrong name takes as long as a wrong password.
    static readonly string DummyHash = PasswordHasher.Hash("unused-dummy-password");

    public bool NeedsSetup()
    {
        using var c = data.OpenHost();
        return UserRepository.Count(c) == 0;
    }

    /// <summary>Creates the first user, the Owner. Allowed only from the host PC, and only once.</summary>
    public Session SetupOwner(string name, string password, string machine, bool isLoopback)
    {
        if (!isLoopback) throw new ServiceException(ServiceError.PermissionDenied, "Set up the Owner on the host PC.");
        CheckPassword(password);
        var cleanName = CleanName(name);
        var hash = PasswordHasher.Hash(password);
        var user = data.WriteHost(tx =>
        {
            if (UserRepository.Count(tx.Connection!) > 0) throw new ServiceException(ServiceError.Invalid, "The Owner is already set up.");
            var id = UserRepository.Insert(tx, cleanName, hash, BuiltInRole.Owner);
            var u = UserRepository.Get(tx.Connection!, id)!;
            AuditLog.Append(tx, new AuditEntry(time.GetUtcNow(), u.Name, machine, ChangeSource.App, null, "setup-owner", "user",
                Key(id), null, UserJson(u)));
            return u;
        });
        return sessions.Create(ContextFor(user, [], machine));
    }

    public Session Login(string name, string password, string machine)
    {
        using var c = data.OpenHost();
        if (UserRepository.Count(c) == 0) throw new ServiceException(ServiceError.SetupRequired, "Set up the Owner first.");
        var cleanName = name.Trim();
        throttle.Check(cleanName);
        var user = UserRepository.Find(c, cleanName);
        var ok = PasswordHasher.Verify(password, user?.PasswordHash ?? DummyHash) && user is { Active: true };
        if (!ok)
        {
            throttle.Failed(cleanName);
            throw new ServiceException(ServiceError.NotAuthenticated, "Wrong user name or password.");
        }
        throttle.Succeeded(cleanName);
        return sessions.Create(ContextFor(user!, UserRepository.Companies(c, user!.Id), machine));
    }

    public void Logout(string token) => sessions.Remove(token);

    /// <summary>Changes the caller's password and ends their other sessions.</summary>
    public void ChangePassword(CallContext ctx, string sessionToken, string current, string next)
    {
        CheckPassword(next);
        var hash = PasswordHasher.Hash(next);
        data.WriteHost(tx =>
        {
            var u = UserRepository.Get(tx.Connection!, ctx.UserId) ?? throw ServiceException.Denied();
            if (!PasswordHasher.Verify(current, u.PasswordHash))
                // Invalid, not NotAuthenticated: the session is still good, only the typed password is wrong.
                throw new ServiceException(ServiceError.Invalid, "Current password is wrong.");
            UserRepository.SetPassword(tx, u.Id, hash);
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "change-password", "user", Key(u.Id), null, null));
            return 0;
        });
        sessions.RemoveUser(ctx.UserId, except: sessionToken);
    }

    internal static CallContext ContextFor(UserRecord u, IReadOnlyList<int> companies, string machine) =>
        new(u.Id, u.Name, Roles.Of(u.Role), companies.ToHashSet(), machine, ChangeSource.App, null, ReadOnly: false,
            AllCompanies: u.Role == BuiltInRole.Owner);

    internal static void CheckPassword(string password)
    {
        if (password.Length < MinPasswordLength) throw new ServiceException(ServiceError.Invalid, "Use at least 8 characters.");
    }

    internal static string CleanName(string name)
    {
        var clean = name.Trim();
        if (clean.Length == 0 || clean.Any(char.IsControl)) throw new ServiceException(ServiceError.Invalid, "Enter a user name.");
        return clean;
    }

    internal static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

    // Never includes the password hash.
    internal static string UserJson(UserRecord u) => JsonSerializer.Serialize(new { u.Name, Role = u.Role.ToString(), u.Active });
}
