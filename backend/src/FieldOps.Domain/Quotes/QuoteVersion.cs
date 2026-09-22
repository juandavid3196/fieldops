namespace FieldOps.Domain.Quotes;

public sealed class QuoteVersion
{
    private QuoteVersion()
    {
    }

    private QuoteVersion(
        Guid id,
        Guid organizationId,
        Guid quoteId,
        int versionNo,
        string scope,
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        string currency,
        Guid createdByUserId)
    {
        Id = id;
        OrganizationId = organizationId;
        QuoteId = quoteId;
        VersionNo = versionNo;
        Scope = scope;
        Subtotal = subtotal;
        TaxTotal = taxTotal;
        Total = total;
        Currency = currency;
        CreatedByUserId = createdByUserId;
        IsImmutable = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid QuoteId { get; private set; }

    public int VersionNo { get; private set; }

    public string Scope { get; private set; } = string.Empty;

    public string? CustomerNotes { get; private set; }

    public string? InternalNotes { get; private set; }

    public decimal Subtotal { get; private set; }

    public decimal TaxTotal { get; private set; }

    public decimal Total { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateOnly? ValidUntil { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public bool IsImmutable { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static QuoteVersion Create(
        Guid organizationId,
        Guid quoteId,
        int versionNo,
        string scope,
        decimal subtotal,
        decimal taxTotal,
        decimal total,
        string currency,
        Guid createdByUserId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (quoteId == Guid.Empty)
        {
            throw new ArgumentException(
                "Quote id is required.",
                nameof(quoteId));
        }

        if (versionNo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionNo),
                versionNo,
                "Version number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException(
                "Quote version scope is required.",
                nameof(scope));
        }

        if (subtotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subtotal),
                subtotal,
                "Subtotal cannot be negative.");
        }

        if (taxTotal < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(taxTotal),
                taxTotal,
                "Tax total cannot be negative.");
        }

        if (total < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                total,
                "Total cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException(
                "Currency is required.",
                nameof(currency));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Created by user id is required.",
                nameof(createdByUserId));
        }

        return new QuoteVersion(
            Guid.NewGuid(),
            organizationId,
            quoteId,
            versionNo,
            scope.Trim(),
            subtotal,
            taxTotal,
            total,
            currency.Trim(),
            createdByUserId);
    }
}
