using FieldOps.Domain.Users;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Resolves the active organization: among active memberships in active
/// organizations, the earliest joined_at (nulls last), then the earliest
/// created_at, then the lowest id.
/// </summary>
public static class ActiveOrganizationSelector
{
    public static MembershipCandidate? Select(IEnumerable<MembershipCandidate> memberships) =>
        memberships
            .Where(membership =>
                membership.MembershipStatus == UserStatus.Active
                && membership.OrganizationIsActive)
            .OrderBy(membership => membership.JoinedAt is null)
            .ThenBy(membership => membership.JoinedAt)
            .ThenBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.MembershipId, UuidComparer.Instance)
            .FirstOrDefault();

    // PostgreSQL orders uuid values by their bytes in network order, which
    // matches the ordinal order of the canonical hexadecimal form.
    private sealed class UuidComparer : IComparer<Guid>
    {
        public static readonly UuidComparer Instance = new();

        public int Compare(Guid x, Guid y) =>
            string.CompareOrdinal(x.ToString("N"), y.ToString("N"));
    }
}
