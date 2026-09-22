namespace FieldOps.Domain.Quotes;

public sealed class Quote
{
    private Quote()
    {
    }

    private Quote(
        Guid id,
        Guid organizationId,
        Guid requestId,
        long quoteNumber,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestId = requestId;
        QuoteNumber = quoteNumber;
        CreatedByUserId = createdByUserId;
        Status = QuoteStatus.Draft;
        CurrentVersionNo = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? BranchId { get; private set; }

    public Guid RequestId { get; private set; }

    public Guid? CustomerId { get; private set; }

    public Guid? PropertyId { get; private set; }

    public long QuoteNumber { get; private set; }

    public QuoteStatus Status { get; private set; }

    public int CurrentVersionNo { get; private set; }

    public Guid? ApprovedVersionId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Quote Create(
        Guid organizationId,
        Guid requestId,
        long quoteNumber,
        Guid createdByUserId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request id is required.",
                nameof(requestId));
        }

        if (quoteNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quoteNumber),
                quoteNumber,
                "Quote number must be greater than zero.");
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new Quote(
            Guid.NewGuid(),
            organizationId,
            requestId,
            quoteNumber,
            createdByUserId);
    }
}
