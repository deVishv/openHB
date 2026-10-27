using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OpenHisabKitab.Host.Application;

public sealed record Session(string Token, CallContext Context, DateTimeOffset ExpiresAt);

/// <summary>Logged in sessions, held in memory. A session ends after 12 hours without use. A host restart ends all of them.</summary>
public sealed class SessionStore(TimeProvider time)
{
    public static readonly TimeSpan IdleLimit = TimeSpan.FromHours(12);

    sealed class Entry(CallContext context, DateTimeOffset expiresAt)
    {
        public CallContext Context { get; } = context;
        public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    }

    readonly ConcurrentDictionary<string, Entry> sessions = new(StringComparer.Ordinal);

    public Session Create(CallContext ctx)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = time.GetUtcNow() + IdleLimit;
        sessions[token] = new Entry(ctx, expires);
        return new Session(token, ctx, expires);
    }

    /// <summary>The caller for a token, or null when unknown or expired. Each use pushes the expiry back.</summary>
    public CallContext? Resolve(string token)
    {
        if (!sessions.TryGetValue(token, out var e)) return null;
        var now = time.GetUtcNow();
        lock (e)
        {
            if (now >= e.ExpiresAt)
            {
                sessions.TryRemove(token, out _);
                return null;
            }
            e.ExpiresAt = now + IdleLimit;
        }
        return e.Context;
    }

    public void Remove(string token) => sessions.TryRemove(token, out _);

    public void RemoveAll() => sessions.Clear();

    /// <summary>Ends every session of a user, except the one named.</summary>
    public void RemoveUser(int userId, string? except = null)
    {
        foreach (var (token, e) in sessions)
            if (e.Context.UserId == userId && token != except) sessions.TryRemove(token, out _);
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
