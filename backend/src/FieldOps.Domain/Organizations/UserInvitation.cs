namespace FieldOps.Domain.Organizations;

public sealed class UserInvitation
{
    private UserInvitation()
    {
    }

    private UserInvitation(
        Guid id,
        Guid organizationId,
        string email,
        short roleId,
        string tokenHash,
        Guid invitedByUserId,
        DateTimeOffset expiresAt)
    {
        Id = id;
        OrganizationId = organizationId;
        Email = email;
        RoleId = roleId;
        TokenHash = tokenHash;
        InvitedByUserId = invitedByUserId;
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public short RoleId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static UserInvitation Create(
        Guid organizationId,
        string email,
        short roleId,
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
            email.Trim(),
            roleId,
            tokenHash,
            invitedByUserId,
            expiresAt);
    }
}
