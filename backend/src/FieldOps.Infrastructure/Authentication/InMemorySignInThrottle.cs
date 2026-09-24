using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FieldOps.Application.Authentication;

namespace FieldOps.Infrastructure.Authentication;

/// <summary>
/// In-memory per-email throttle: after 5 failed attempts within a sliding
/// 15-minute window the email is throttled until the oldest counted failure
/// leaves the window. Keys are SHA-256 hashes of the normalized email.
/// Counters are per process, lost on restart and not shared across
/// instances; concurrent attempts may briefly exceed the limit.
/// </summary>
public sealed class InMemorySignInThrottle(TimeProvider timeProvider) : ISignInThrottle
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _failures =
        new(StringComparer.Ordinal);

    private readonly Lock _sweepLock = new();

    private DateTimeOffset _lastSweep = timeProvider.GetUtcNow();

    public TimeSpan? GetRetryAfter(string normalizedEmail)
    {
        if (!_failures.TryGetValue(ToKey(normalizedEmail), out var failures))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        lock (failures)
        {
            Prune(failures, now);

            if (failures.Count < MaxFailures)
            {
                return null;
            }

            // The throttle lifts once only MaxFailures - 1 failures remain.
            var releasedAt = failures[failures.Count - MaxFailures] + Window;
            var retryAfter = releasedAt - now;

            return retryAfter > TimeSpan.Zero ? retryAfter : null;
        }
    }

    public void RecordFailure(string normalizedEmail)
    {
        var now = timeProvider.GetUtcNow();
        var failures = _failures.GetOrAdd(ToKey(normalizedEmail), _ => []);

        lock (failures)
        {
            Prune(failures, now);
            failures.Add(now);
        }

        SweepIfDue(now);
    }

    public void Reset(string normalizedEmail) =>
        _failures.TryRemove(ToKey(normalizedEmail), out _);

    private static void Prune(List<DateTimeOffset> failures, DateTimeOffset now) =>
        failures.RemoveAll(failedAt => failedAt + Window <= now);

    private static string ToKey(string normalizedEmail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));

    // Removes keys whose failures all left the window, at most once per window.
    private void SweepIfDue(DateTimeOffset now)
    {
        lock (_sweepLock)
        {
            if (now - _lastSweep < Window)
            {
                return;
            }

            _lastSweep = now;
        }

        foreach (var (key, failures) in _failures)
        {
            bool empty;

            lock (failures)
            {
                Prune(failures, now);
                empty = failures.Count == 0;
            }

            if (empty)
            {
                _failures.TryRemove(new KeyValuePair<string, List<DateTimeOffset>>(key, failures));
            }
        }
    }
}
