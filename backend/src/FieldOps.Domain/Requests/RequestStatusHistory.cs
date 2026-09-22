namespace FieldOps.Domain.Requests;

public sealed class RequestStatusHistory
{
    private RequestStatusHistory()
    {
    }

    private RequestStatusHistory(
        Guid id,
        Guid organizationId,
        Guid requestId,
        RequestStatus? fromStatus,
        RequestStatus toStatus)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestId = requestId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid RequestId { get; private set; }

    public RequestStatus? FromStatus { get; private set; }

    public RequestStatus ToStatus { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public static RequestStatusHistory Create(
        Guid organizationId,
        Guid requestId,
        RequestStatus? fromStatus,
        RequestStatus toStatus)
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

        return new RequestStatusHistory(
            Guid.NewGuid(),
            organizationId,
            requestId,
            fromStatus,
            toStatus);
    }
}
