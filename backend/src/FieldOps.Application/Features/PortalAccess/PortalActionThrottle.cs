namespace FieldOps.Application.Features.PortalAccess;

public enum PortalAction
{
    ServiceRequest,
    PropertyCreate,
    Message,
    Reschedule,
}

/// <summary>
/// Per-user limits of the portal writes (customer portal BR-28, BR-33, BR-35): service requests 5, property creation 10,
/// messages 5 and reschedule requests 5 per 15 minutes. The rate limiter runs before authentication, so the per-user
/// part is enforced here, keyed by action and user id.
/// </summary>
public interface IPortalActionThrottle
{
    /// <summary>Counts the attempt and returns the time until the user may try again, or null when it is allowed.</summary>
    TimeSpan? TryAcquire(PortalAction action, Guid userId);
}

public static class PortalActionLimits
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static int Limit(PortalAction action) => action == PortalAction.PropertyCreate ? 10 : 5;
}
