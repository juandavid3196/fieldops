using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Features.PasswordResets;

namespace FieldOps.Infrastructure.PasswordResets;

/// <summary>
/// In-memory per-email request limit (BR-12): 3 requests per fixed 60-minute
/// window per normalized email. Keys are SHA-256 hashes, never logged.
/// Counters are per process, lost on restart and not shared across
/// instances. The table is bounded: expired windows are swept periodically
/// and, when the cap is still reached, the oldest windows are evicted so an
/// attacker cannot make the table grow without limit.
/// </summary>
public sealed class InMemoryPasswordResetEmailThrottle(TimeProvider timeProvider) : IPasswordResetEmailThrottle
{
    public const int MaxRequests = 3;

    public const int MaxKeys = 50_000;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, Counter> _counters = new(StringComparer.Ordinal);

    private readonly Lock _lock = new();

    private DateTimeOffset _lastSweep = timeProvider.GetUtcNow();

    public bool TryAcquire(string normalizedEmail)
    {
        var key = ToKey(normalizedEmail);
        var now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (now - _lastSweep >= SweepInterval || _counters.Count >= MaxKeys)
            {
                Sweep(now);
            }

            if (_counters.TryGetValue(key, out var counter) && now - counter.WindowStart < Window)
            {
                if (counter.Count >= MaxRequests)
                {
                    return false;
                }

                _counters[key] = counter with { Count = counter.Count + 1 };

                return true;
            }

            _counters[key] = new Counter(now, 1);

            return true;
        }
    }

    private static string ToKey(string normalizedEmail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));

    // Caller holds the lock.
    private void Sweep(DateTimeOffset now)
    {
        _lastSweep = now;

        foreach (var (key, counter) in _counters.ToArray())
        {
            if (now - counter.WindowStart >= Window)
            {
                _counters.Remove(key);
            }
        }

        if (_counters.Count < MaxKeys)
        {
            return;
        }

        // Still full of live windows: evict the oldest tenth.
        foreach (var (key, _) in _counters.OrderBy(pair => pair.Value.WindowStart).Take(MaxKeys / 10).ToArray())
        {
            _counters.Remove(key);
        }
    }

    private readonly record struct Counter(DateTimeOffset WindowStart, int Count);
}
