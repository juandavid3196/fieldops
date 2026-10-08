namespace FieldOps.Domain.Invoices;

/// <summary>
/// The hashed secret of the public invoice link (invoice-draft-delivery BR-18, SA-10), scoped to one invoice. The raw
/// token is never part of the entity.
/// </summary>
public sealed class InvoiceAccessToken
{
    private InvoiceAccessToken()
    {
    }

    private InvoiceAccessToken(
        Guid id,
        Guid organizationId,
        Guid invoiceId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        InvoiceId = invoiceId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static InvoiceAccessToken Create(
        Guid organizationId,
        Guid invoiceId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Invoice id is required.",
                nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException(
                "Token hash is required.",
                nameof(tokenHash));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        var createdAt = new DateTimeOffset(now.UtcDateTime.Ticks - (now.UtcDateTime.Ticks % 10), TimeSpan.Zero);

        if (expiresAt <= createdAt)
        {
            throw new ArgumentException(
                "The token must expire after it is created.",
                nameof(expiresAt));
        }

        return new InvoiceAccessToken(
            Guid.NewGuid(),
            organizationId,
            invoiceId,
            tokenHash.Trim(),
            expiresAt,
            createdByUserId,
            createdAt);
    }
}
