using System.Collections.Concurrent;
using FieldOps.Application.Features.PortalAccess;

namespace FieldOps.Infrastructure.Authentication;

/// <summary>
/// In-memory per-user limits of the portal writes (customer portal BR-28, BR-33, BR-35): a sliding 15-minute window per action
/// and user id. Every acquisition counts, whatever its outcome. Counters are per process, lost on restart and not shared
/// across instances.
/// </summary>
public sealed class InMemoryPortalActionThrottle(TimeProvider timeProvider) : IPortalActionThrottle
{
    private readonly ConcurrentDictionary<(PortalAction Action, Guid UserId), List<DateTimeOffset>> requests = new();

    private readonly Lock sweepLock = new();

    private DateTimeOffset lastSweep = timeProvider.GetUtcNow();

    public TimeSpan? TryAcquire(PortalAction action, Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var window = PortalActionLimits.Window;
        var limit = PortalActionLimits.Limit(action);
        var entries = requests.GetOrAdd((action, userId), _ => []);
        TimeSpan? retryAfter = null;

        lock (entries)
        {
            entries.RemoveAll(at => at + window <= now);

            if (entries.Count >= limit)
            {
                retryAfter = entries[0] + window - now;
            }
            else
            {
                entries.Add(now);
            }
        }

        SweepIfDue(now);

        return retryAfter;
    }

    // Removes keys whose entries all left the window, at most once per window.
    private void SweepIfDue(DateTimeOffset now)
    {
        lock (sweepLock)
        {
            if (now - lastSweep < PortalActionLimits.Window)
            {
                return;
            }

            lastSweep = now;
        }

        foreach (var (key, entries) in requests)
        {
            bool empty;

            lock (entries)
            {
                entries.RemoveAll(at => at + PortalActionLimits.Window <= now);
                empty = entries.Count == 0;
            }

            if (empty)
            {
                requests.TryRemove(key, out _);
            }
        }
    }
}
