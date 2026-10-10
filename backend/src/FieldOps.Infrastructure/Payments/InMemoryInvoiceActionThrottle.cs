using System.Collections.Concurrent;
using FieldOps.Application.Features.OnlinePayments;

namespace FieldOps.Infrastructure.Payments;

/// <summary>
/// In-memory per-invoice limits of the public actions (customer-invoice-payments BR-23): card intent 10, bank transfer
/// notice 3 and review 3 per sliding hour, keyed by action and invoice id. Every acquisition counts, whatever its outcome.
/// Counters are per process, lost on restart and not shared across instances.
/// </summary>
public sealed class InMemoryInvoiceActionThrottle(TimeProvider timeProvider) : IInvoiceActionThrottle
{
    private readonly ConcurrentDictionary<(InvoiceAction Action, Guid InvoiceId), List<DateTimeOffset>> requests = new();

    private readonly Lock sweepLock = new();

    private DateTimeOffset lastSweep = timeProvider.GetUtcNow();

    public TimeSpan? TryAcquire(InvoiceAction action, Guid invoiceId)
    {
        var now = timeProvider.GetUtcNow();
        var window = InvoiceActionLimits.Window;
        var limit = InvoiceActionLimits.Limit(action);
        var entries = requests.GetOrAdd((action, invoiceId), _ => []);
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
            if (now - lastSweep < InvoiceActionLimits.Window)
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
                entries.RemoveAll(at => at + InvoiceActionLimits.Window <= now);
                empty = entries.Count == 0;
            }

            if (empty)
            {
                requests.TryRemove(new KeyValuePair<(InvoiceAction Action, Guid InvoiceId), List<DateTimeOffset>>(key, entries));
            }
        }
    }
}
