using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenHisabKitab.Data.Audit;
using OpenHisabKitab.Data.HostDb;
using OpenHisabKitab.Host.Grpc;

namespace OpenHisabKitab.Host.Application;

/// <summary>A new token. Token is the only time the token text is ever shown.</summary>
public sealed record CreatedToken(int Id, string Name, bool ReadWrite, string Token);

/// <summary>
/// API tokens for the local REST API and MCP (spec 4.1, 8). A token acts as its user, further limited to reading unless
/// it was created read and write. host.db keeps only a SHA-256 hash: a token is 256 random bits, so a slow hash adds nothing.
/// </summary>
public sealed class ApiTokenService(HostData data, TimeProvider time)
{
    public const int MaxNameLength = 60;
    const string Prefix = "ohk_";
    static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(60);

    // Failed tokens are slowed per caller address. A valid token always passes, so one script on the host PC sending a
    // bad token cannot lock out every other program there.
    readonly LoginThrottle throttle = new(time);

    public CreatedToken Create(CallContext ctx, string name, bool readWrite)
    {
        RequireApp(ctx);
        var clean = name.Trim();
        if (clean.Length is 0 or > MaxNameLength || clean.Any(char.IsControl))
            throw new ServiceException(ServiceError.Invalid, $"Enter a token name of up to {MaxNameLength} characters.");
        var token = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var id = data.WriteHost(tx =>
        {
            if (ApiTokenRepository.ListForUser(tx.Connection!, ctx.UserId)
                .Any(t => t.RevokedAt is null && string.Equals(t.Name, clean, StringComparison.OrdinalIgnoreCase)))
                throw new ServiceException(ServiceError.Invalid, $"You already have a token named {clean}.");
            var now = time.GetUtcNow();
            var newId = ApiTokenRepository.Insert(tx, ctx.UserId, clean, Hash(token), readWrite, now);
            AuditLog.Append(tx, ctx.Audit(now, "create-token", "api-token", AuthService.Key(newId), null, Json(clean, readWrite)));
            return newId;
        });
        return new CreatedToken(id, clean, readWrite, token);
    }

    /// <summary>The caller's own tokens, newest first, revoked ones included.</summary>
    public IReadOnlyList<ApiTokenRecord> List(CallContext ctx)
    {
        using var c = data.OpenHost();
        return ApiTokenRepository.ListForUser(c, ctx.UserId);
    }

    public void Revoke(CallContext ctx, int id)
    {
        RequireApp(ctx);
        data.WriteHost(tx =>
        {
            var t = ApiTokenRepository.Get(tx.Connection!, id);
            if (t is null || t.UserId != ctx.UserId) throw new ServiceException(ServiceError.NotFound, "No such token.");
            var now = time.GetUtcNow();
            if (ApiTokenRepository.Revoke(tx, id, now))
                AuditLog.Append(tx, ctx.Audit(now, "revoke-token", "api-token", AuthService.Key(id), Json(t.Name, t.ReadWrite), null));
            return 0;
        });
    }

    /// <summary>The caller behind a bearer token on the API port. The user, role, and companies are read afresh each time.</summary>
    public CallContext Authenticate(string? bearer, ChangeSource source, IPAddress? remote)
    {
        var invalid = new ServiceException(ServiceError.NotAuthenticated, "Send a valid API token.");
        if (string.IsNullOrEmpty(bearer)) throw invalid;
        var key = "api:" + (remote?.ToString() ?? "unknown");
        using var c = data.OpenHost();
        var found = ApiTokenRepository.FindByHash(c, Hash(bearer));
        var token = found?.Token;
        var user = token is null ? null : UserRepository.Get(c, token.UserId);
        if (token is null || token.RevokedAt is not null || user is not { Active: true })
        {
            throttle.Check(key);
            throttle.Failed(key);
            throw invalid;
        }
        throttle.Succeeded(key);

        var now = time.GetUtcNow();
        if (token.LastUsedAt is not { } used || now - used > TouchInterval) ApiTokenRepository.Touch(c, token.Id, now);

        var machine = Loopback.Is(remote) ? Environment.MachineName : remote?.ToString() ?? "unknown";
        var ctx = AuthService.ContextFor(user, UserRepository.Companies(c, user.Id), machine) with { Source = source, TokenName = token.Name };
        if (!token.ReadWrite)
            return ctx with { ReadOnly = true, ReadOnlyReason = "This token is read only. Create a read and write token to make changes." };
        if (source == ChangeSource.Mcp && !ApiSettings.Read(c).McpAllowWrites)
            return ctx with { ReadOnly = true, ReadOnlyReason = "Changes through MCP are off. The Owner can turn on mcp.allowWrites." };
        return ctx;
    }

    // A token cannot mint or revoke tokens, or a read only token could make itself a read and write one.
    static void RequireApp(CallContext ctx)
    {
        if (ctx.Source != ChangeSource.App)
            throw new ServiceException(ServiceError.PermissionDenied, "Create and revoke tokens in Open Hisab Kitab.");
    }

    static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    static string Json(string name, bool readWrite) => JsonSerializer.Serialize(new { Name = name, ReadWrite = readWrite });
}
