namespace FieldOps.Domain.Customers;

/// <summary>
/// An invitation for a customer's primary contact to activate portal access (customer portal BR-11). Only the
/// SHA-256 hex of the token is stored; the raw token is never persisted.
/// </summary>
public sealed class CustomerPortalInvitation
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private CustomerPortalInvitation()
    {
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid ContactId { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public Guid InvitedByUserId { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CustomerPortalInvitation Create(
        Guid organizationId,
        Guid contactId,
        string email,
        string tokenHash,
        Guid invitedByUserId,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || contactId == Guid.Empty || invitedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Organization, contact and inviter are required.");
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Email and token hash are required.");
        }

        return new CustomerPortalInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ContactId = contactId,
            Email = email.Trim().ToLowerInvariant(),
            TokenHash = tokenHash,
            InvitedByUserId = invitedByUserId,
            ExpiresAt = now + Lifetime,
            CreatedAt = now,
        };
    }

    public bool IsOpen(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void Accept(DateTimeOffset now) => AcceptedAt = now;
}
