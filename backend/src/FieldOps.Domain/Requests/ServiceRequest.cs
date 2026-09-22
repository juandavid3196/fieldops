namespace FieldOps.Domain.Requests;

public sealed class ServiceRequest
{
    private ServiceRequest()
    {
    }

    private ServiceRequest(
        Guid id,
        Guid organizationId,
        long requestNumber,
        string description)
    {
        Id = id;
        OrganizationId = organizationId;
        RequestNumber = requestNumber;
        Description = description;
        Status = RequestStatus.New;
        Source = "public_form";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid? BranchId { get; private set; }

    public long RequestNumber { get; private set; }

    public Guid? CustomerId { get; private set; }

    public Guid? ContactId { get; private set; }

    public Guid? PropertyId { get; private set; }

    public Guid? CategoryId { get; private set; }

    public string? GuestName { get; private set; }

    public string? GuestEmail { get; private set; }

    public string? GuestPhone { get; private set; }

    public string? ServiceAddress { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public DateTimeOffset? PreferredStart { get; private set; }

    public DateTimeOffset? PreferredEnd { get; private set; }

    public RequestStatus Status { get; private set; }

    public string Source { get; private set; } = "public_form";

    public Guid? AssignedDispatcherUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static ServiceRequest Create(
        Guid organizationId,
        long requestNumber,
        string description,
        DateTimeOffset? preferredStart = null,
        DateTimeOffset? preferredEnd = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (requestNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestNumber),
                requestNumber,
                "Request number must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Request description is required.",
                nameof(description));
        }

        if (preferredStart is not null
            && preferredEnd is not null
            && preferredStart >= preferredEnd)
        {
            throw new ArgumentException(
                "Preferred start must be before preferred end.",
                nameof(preferredStart));
        }

        return new ServiceRequest(
            Guid.NewGuid(),
            organizationId,
            requestNumber,
            description.Trim())
        {
            PreferredStart = preferredStart,
            PreferredEnd = preferredEnd,
        };
    }
}
