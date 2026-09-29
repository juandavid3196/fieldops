using FieldOps.Domain.Users;

namespace FieldOps.Domain.Organizations;

public sealed class OrganizationUser
{
    private OrganizationUser()
    {
    }

    private OrganizationUser(
        Guid id,
        Guid organizationId,
        Guid userId,
        short roleId)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
        Status = UserStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public short RoleId { get; private set; }

    public UserStatus Status { get; private set; }

    public bool IsAllBranches { get; private set; }

    public Guid? InvitedByUserId { get; private set; }

    public DateTimeOffset? JoinedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static OrganizationUser Create(
        Guid organizationId,
        Guid userId,
        short roleId,
        bool isAllBranches = false,
        DateTimeOffset? joinedAt = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User id is required.",
                nameof(userId));
        }

        if (roleId <= 0)
        {
            throw new ArgumentException(
                "Role id is required.",
                nameof(roleId));
        }

        return new OrganizationUser(
            Guid.NewGuid(),
            organizationId,
            userId,
            roleId)
        {
            IsAllBranches = isAllBranches,
            JoinedAt = joinedAt,
        };
    }

    /// <summary>Active to suspended; returns false when already suspended.</summary>
    public bool Suspend(DateTimeOffset now)
    {
        if (Status == UserStatus.Suspended)
        {
            return false;
        }

        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException("Only an active membership can be suspended.");
        }

        Status = UserStatus.Suspended;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Suspended to active; returns false when already active.</summary>
    public bool Reactivate(DateTimeOffset now)
    {
        if (Status == UserStatus.Active)
        {
            return false;
        }

        if (Status != UserStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended membership can be reactivated.");
        }

        Status = UserStatus.Active;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Returns whether the role or all-branches flag changed.</summary>
    public bool ChangeAccess(short roleId, bool isAllBranches, DateTimeOffset now)
    {
        if (roleId <= 0)
        {
            throw new ArgumentException(
                "Role id is required.",
                nameof(roleId));
        }

        if (RoleId == roleId && IsAllBranches == isAllBranches)
        {
            return false;
        }

        RoleId = roleId;
        IsAllBranches = isAllBranches;
        UpdatedAt = now;

        return true;
    }
}
