using System.Collections.Concurrent;
using FieldOps.Application.Features.Messaging;

namespace FieldOps.Infrastructure.Authentication;

/// <summary>
/// In-memory limit of message sends (technician-messages BR-09): a sliding one-minute window of 30 per organization and
/// user. Every acquisition counts. Counters are per process, lost on restart and not shared across instances.
/// </summary>
public sealed class InMemoryMessageSendThrottle(TimeProvider timeProvider) : IMessageSendThrottle
{
    private readonly ConcurrentDictionary<(Guid OrganizationId, Guid UserId), List<DateTimeOffset>> requests = new();

    private readonly Lock sweepLock = new();

    private DateTimeOffset lastSweep = timeProvider.GetUtcNow();

    public TimeSpan? TryAcquire(Guid organizationId, Guid userId)
    {
        var now = timeProvider.GetUtcNow();
        var entries = requests.GetOrAdd((organizationId, userId), _ => []);
        TimeSpan? retryAfter = null;

        lock (entries)
        {
            entries.RemoveAll(at => at + MessageSendLimits.Window <= now);

            if (entries.Count >= MessageSendLimits.PerWindow)
            {
                retryAfter = entries[0] + MessageSendLimits.Window - now;
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
            if (now - lastSweep < MessageSendLimits.Window)
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
                entries.RemoveAll(at => at + MessageSendLimits.Window <= now);
                empty = entries.Count == 0;
            }

            if (empty)
            {
                requests.TryRemove(key, out _);
            }
        }
    }
}
