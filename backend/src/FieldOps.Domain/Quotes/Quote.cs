namespace FieldOps.Domain.Quotes;

public sealed class Quote
{
    private Quote()
    {
    }

    private Quote(
        Guid id,
        Guid organizationId,
        Guid? branchId,
        Guid requestId,
        Guid? customerId,
        Guid? propertyId,
        long quoteNumber,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        RequestId = requestId;
        CustomerId = customerId;
        PropertyId = propertyId;
        QuoteNumber = quoteNumber;
        CreatedByUserId = createdByUserId;
        Status = QuoteStatus.Draft;
        CurrentVersionNo = 0;
        CreatedAt = Truncate(DateTimeOffset.UtcNow);
        UpdatedAt = CreatedAt;
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

    /// <summary>Optimistic concurrency token (quote-builder BR-22): every mutation sets a new value.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Quote Create(
        Guid organizationId,
        Guid? branchId,
        Guid requestId,
        Guid? customerId,
        Guid? propertyId,
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
            branchId,
            requestId,
            customerId,
            propertyId,
            quoteNumber,
            createdByUserId);
    }

    /// <summary>Sets a new concurrency value, truncated to microseconds (the PostgreSQL precision) and always different from the previous one.</summary>
    public void Touch(DateTimeOffset now)
    {
        var next = Truncate(now);

        if (next <= UpdatedAt)
        {
            next = UpdatedAt.AddTicks(10);
        }

        UpdatedAt = next;
    }

    public void MarkSent(int versionNo)
    {
        if (Status is QuoteStatus.Cancelled)
        {
            throw new InvalidOperationException("A cancelled quote cannot be sent.");
        }

        if (versionNo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNo),
                versionNo,
                "Version number must be greater than zero.");
        }

        Status = QuoteStatus.Sent;
        CurrentVersionNo = versionNo;
    }

    /// <summary>Cancels a quote that was never sent; its number is never reused.</summary>
    public void Cancel()
    {
        if (Status != QuoteStatus.Draft)
        {
            throw new InvalidOperationException("Only a draft quote can be cancelled.");
        }

        Status = QuoteStatus.Cancelled;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;

        return new DateTimeOffset(utc.Ticks - (utc.Ticks % 10), TimeSpan.Zero);
    }
}
