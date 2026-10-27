using System.Text.Json;
using Microsoft.Data.Sqlite;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;

namespace OpenHisabKitab.Host.Application;

/// <summary>User administration with the built in roles. Custom roles come in Plan 8.</summary>
public sealed class UserService(HostData data, SessionStore sessions, TimeProvider time)
{
    public int Create(CallContext ctx, string name, string password, BuiltInRole role)
    {
        ctx.Require(Permission.ManageUsers);
        AuthService.CheckPassword(password);
        var cleanName = AuthService.CleanName(name);
        var hash = PasswordHasher.Hash(password);
        return data.WriteHost(tx =>
        {
            if (UserRepository.Find(tx.Connection!, cleanName) is not null)
                throw new ServiceException(ServiceError.Invalid, $"User {cleanName} already exists.");
            var id = UserRepository.Insert(tx, cleanName, hash, role);
            var u = UserRepository.Get(tx.Connection!, id)!;
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "create", "user", AuthService.Key(id), null, AuthService.UserJson(u)));
            return id;
        });
    }

    /// <summary>Sets which companies a user can open. The user's sessions end so the change applies at their next login.</summary>
    public void SetCompanies(CallContext ctx, int userId, IReadOnlyList<int> companies)
    {
        ctx.Require(Permission.ManageUsers);
        data.WriteHost(tx =>
        {
            if (UserRepository.Get(tx.Connection!, userId) is null) throw new ServiceException(ServiceError.NotFound, "No such user.");
            var before = UserRepository.Companies(tx.Connection!, userId);
            try { UserRepository.SetCompanies(tx, userId, companies); }
            catch (SqliteException) { throw new ServiceException(ServiceError.Invalid, "Unknown company."); }
            AuditLog.Append(tx, ctx.Audit(time.GetUtcNow(), "set-companies", "user", AuthService.Key(userId),
                JsonSerializer.Serialize(before), JsonSerializer.Serialize(companies.Distinct().Order())));
            return 0;
        });
        sessions.RemoveUser(userId);
    }

    public IReadOnlyList<UserRecord> List(CallContext ctx)
    {
        ctx.Require(Permission.ManageUsers);
        using var c = data.OpenHost();
        return [.. UserRepository.List(c).Select(u => u with { PasswordHash = "" })];
    }
}
