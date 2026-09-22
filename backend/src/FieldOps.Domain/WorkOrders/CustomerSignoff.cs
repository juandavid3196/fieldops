namespace FieldOps.Domain.WorkOrders;

public sealed class CustomerSignoff
{
    private CustomerSignoff()
    {
    }

    private CustomerSignoff(Guid id, Guid visitId, bool accepted)
    {
        Id = id;
        VisitId = visitId;
        Accepted = accepted;
        SignedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid VisitId { get; private set; }

    public string? SignerName { get; private set; }

    public Guid? SignerContactId { get; private set; }

    // The signature itself is optional: a signoff may instead carry an
    // AbsenceReason when the customer was not present to sign.
    public string? SignatureStorageKey { get; private set; }

    public bool Accepted { get; private set; }

    public string? Comments { get; private set; }

    public string? AbsenceReason { get; private set; }

    public DateTimeOffset SignedAt { get; private set; }

    public static CustomerSignoff Create(Guid visitId, bool accepted)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException(
                "Visit id is required.",
                nameof(visitId));
        }

        return new CustomerSignoff(Guid.NewGuid(), visitId, accepted);
    }
}
