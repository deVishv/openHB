using System.Globalization;

namespace OpenHisabKitab.Host.Application;

/// <summary>
/// Slows password guessing per user name: after 5 failures, logins wait 30 seconds, and each further failure doubles
/// the wait up to 15 minutes. A success clears the count. At most 10,000 names are remembered.
/// </summary>
public sealed class LoginThrottle(TimeProvider time)
{
    public const int Capacity = 10_000;
    const int FreeAttempts = 5;
    static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(30);
    static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(15);

    sealed record State(int Failures, DateTimeOffset LockedUntil);

    readonly Dictionary<string, State> states = new(StringComparer.OrdinalIgnoreCase);

    internal int Count { get { lock (states) return states.Count; } }

    public void Check(string name)
    {
        lock (states)
        {
            if (!states.TryGetValue(name, out var s)) return;
            var left = s.LockedUntil - time.GetUtcNow();
            if (left <= TimeSpan.Zero) return;
            var seconds = (long)Math.Ceiling(left.TotalSeconds);
            throw new ServiceException(ServiceError.NotAuthenticated,
                string.Create(CultureInfo.InvariantCulture, $"Too many attempts. Try again in {seconds} seconds."));
        }
    }

    public void Failed(string name)
    {
        lock (states)
        {
            if (!states.ContainsKey(name) && states.Count >= Capacity) Prune();
            var failures = (states.TryGetValue(name, out var s) ? s.Failures : 0) + 1;
            var until = DateTimeOffset.MinValue;
            if (failures >= FreeAttempts)
            {
                var doublings = Math.Min(failures - FreeAttempts, 10);
                var wait = TimeSpan.FromTicks(Math.Min(FirstWait.Ticks << doublings, LongestWait.Ticks));
                until = time.GetUtcNow() + wait;
            }
            states[name] = new State(failures, until);
        }
    }

    // Called at capacity: forget names that are not locked out, then, if still full, the locks that end soonest.
    void Prune()
    {
        var now = time.GetUtcNow();
        foreach (var name in states.Where(kv => kv.Value.LockedUntil <= now).Select(kv => kv.Key).ToList()) states.Remove(name);
        if (states.Count < Capacity) return;
        foreach (var name in states.OrderBy(kv => kv.Value.LockedUntil).Take(states.Count - Capacity + 1).Select(kv => kv.Key).ToList())
            states.Remove(name);
    }

    public void Succeeded(string name)
    {
        lock (states) states.Remove(name);
    }
}
