namespace FieldOps.Domain.Invoices;

/// <summary>SA-18: the single immutable service review of a work order, submitted from the paid invoice (BR-22).</summary>
public sealed class InvoiceReview
{
    public const int MaxCommentLength = 500;

    private InvoiceReview()
    {
    }

    private InvoiceReview(
        Guid id, Guid organizationId, Guid workOrderId, Guid invoiceId, Guid? technicianId, short rating, string? comment, DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        WorkOrderId = workOrderId;
        InvoiceId = invoiceId;
        TechnicianId = technicianId;
        Rating = rating;
        Comment = comment;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public Guid? TechnicianId { get; private set; }

    public short Rating { get; private set; }

    public string? Comment { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static InvoiceReview Create(
        Guid organizationId, Guid workOrderId, Guid invoiceId, Guid? technicianId, int rating, string? comment, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException("Work order id is required.", nameof(workOrderId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        }

        if (rating is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "Rating must be between 1 and 5.");
        }

        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (trimmed is { Length: > MaxCommentLength })
        {
            throw new ArgumentException("Comment must be 500 characters or fewer.", nameof(comment));
        }

        return new InvoiceReview(Guid.NewGuid(), organizationId, workOrderId, invoiceId, technicianId, (short)rating, trimmed, now);
    }
}
