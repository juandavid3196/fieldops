using System.Collections.Concurrent;
using FieldOps.Application.Authentication;

namespace FieldOps.Infrastructure.Authentication;

/// <summary>
/// In-memory per-user throttle of failed current-password verifications
/// (account settings BR-06): 5 failures within a fixed 15-minute window that
/// starts at the first counted failure. A success never resets the count.
/// Counters are per process, lost on restart and not shared across instances;
/// concurrent attempts may briefly exceed the limit.
/// </summary>
public sealed class InMemoryAccountPasswordThrottle(TimeProvider timeProvider) : IAccountPasswordThrottle
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    private readonly Lock _sweepLock = new();

    private DateTimeOffset _lastSweep = timeProvider.GetUtcNow();

    public TimeSpan? GetRetryAfter(Guid userId)
    {
        if (!_entries.TryGetValue(userId, out var entry))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();

        lock (entry)
        {
            if (entry.Count < MaxFailures || now >= entry.WindowStart + Window)
            {
                return null;
            }

            return entry.WindowStart + Window - now;
        }
    }

    public void RecordFailure(Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var entry = _entries.GetOrAdd(userId, _ => new Entry(now));

        lock (entry)
        {
            if (now >= entry.WindowStart + Window)
            {
                entry.WindowStart = now;
                entry.Count = 0;
            }

            entry.Count++;
        }

        SweepIfDue(now);
    }

    // Removes entries whose window ended, at most once per window.
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

        foreach (var (userId, entry) in _entries)
        {
            bool expired;

            lock (entry)
            {
                expired = now >= entry.WindowStart + Window;
            }

            if (expired)
            {
                _entries.TryRemove(new KeyValuePair<Guid, Entry>(userId, entry));
            }
        }
    }

    private sealed class Entry(DateTimeOffset windowStart)
    {
        public DateTimeOffset WindowStart { get; set; } = windowStart;

        public int Count { get; set; }
    }
}
