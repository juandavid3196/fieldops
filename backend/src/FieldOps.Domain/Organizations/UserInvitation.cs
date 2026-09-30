namespace FieldOps.Domain.Organizations;

public sealed class UserInvitation
{
    public const int NameMaxLength = 100;

    private UserInvitation()
    {
    }

    private UserInvitation(
        Guid id,
        Guid organizationId,
        string email,
        string firstName,
        string lastName,
        short roleId,
        bool isAllBranches,
        bool linkTeamProfile,
        string tokenHash,
        Guid invitedByUserId,
        DateTimeOffset expiresAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        RoleId = roleId;
        IsAllBranches = isAllBranches;
        LinkTeamProfile = linkTeamProfile;
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public short RoleId { get; private set; }

    public bool IsAllBranches { get; private set; }

    public bool LinkTeamProfile { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Not accepted and not revoked (an expired invitation is still open).</summary>
    public bool IsOpen => AcceptedAt is null && RevokedAt is null;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public static UserInvitation Create(
        Guid organizationId,
        string email,
        string firstName,
        string lastName,
        short roleId,
        bool isAllBranches,
        bool linkTeamProfile,
        string tokenHash,
        Guid invitedByUserId,
        DateTimeOffset expiresAt)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "Invitation email is required.",
                nameof(email));
        }

        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException(
                "Invitation first name is required and at most 100 characters.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException(
                "Invitation last name is required and at most 100 characters.",
                nameof(lastName));
        }

        if (roleId <= 0)
        {
            throw new ArgumentException(
                "Role id is required.",
                nameof(roleId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException(
                "Invitation token hash is required.",
                nameof(tokenHash));
        }

        if (invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inviting user id is required.",
                nameof(invitedByUserId));
        }

        return new UserInvitation(
            Guid.NewGuid(),
            organizationId,
            email.Trim().ToLowerInvariant(),
            firstName.Trim(),
            lastName.Trim(),
            roleId,
            isAllBranches,
            linkTeamProfile,
            tokenHash,
            invitedByUserId,
            expiresAt);
    }

    /// <summary>Replaces the token hash and expiry; the previous link stops working.</summary>
    public void Resend(string newTokenHash, DateTimeOffset expiresAt)
    {
        EnsureOpen();

        if (string.IsNullOrWhiteSpace(newTokenHash))
        {
            throw new ArgumentException(
                "Invitation token hash is required.",
                nameof(newTokenHash));
        }

        TokenHash = newTokenHash;
        ExpiresAt = expiresAt;
    }

    public void Revoke(DateTimeOffset now)
    {
        EnsureOpen();
        RevokedAt = now;
    }

    /// <summary>Open and not expired (the organization check lives with the caller).</summary>
    public bool IsUsable(DateTimeOffset now) => IsOpen && !IsExpired(now);

    /// <summary>Marks the invitation consumed; it can never be used again.</summary>
    public void Accept(DateTimeOffset now)
    {
        if (!IsUsable(now))
        {
            throw new InvalidOperationException("The invitation can no longer be accepted.");
        }

        AcceptedAt = now;
    }

    /// <summary>Returns whether anything changed.</summary>
    public bool UpdateAccess(short roleId, bool isAllBranches)
    {
        EnsureOpen();

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

        return true;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The invitation is no longer pending.");
        }
    }
}
