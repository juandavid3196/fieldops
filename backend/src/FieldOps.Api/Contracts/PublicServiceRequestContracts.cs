namespace FieldOps.Api.Contracts;

public sealed record PublicFormServiceResponse(Guid Id, string Name);

public sealed record PublicFormCategoryResponse(
    Guid Id,
    string Name,
    IReadOnlyList<PublicFormServiceResponse> Services);

/// <summary>Anonymous form configuration (FR-01): never prices, costs, taxes or internal ids.</summary>
public sealed record PublicServiceRequestFormResponse(
    string OrganizationName,
    string? Phone,
    string? Website,
    string RequestPrefix,
    string Timezone,
    IReadOnlyList<PublicFormCategoryResponse> Categories);

public sealed record PublicServiceRequestCreatedResponse(string RequestNumber);

/// <summary>
/// JSON of the <c>request</c> multipart part. Organization or branch
/// identifiers are not part of the contract; any extra field is ignored (BR-02).
/// </summary>
public sealed class PublicServiceRequestBody
{
    public ContactBody? Contact { get; init; }

    public PropertyBody? Property { get; init; }

    public ServiceBody? Service { get; init; }

    public AvailabilityBody? Availability { get; init; }

    public bool Consent { get; init; }

    public string? Website { get; init; }

    public sealed class ContactBody
    {
        public string? FirstName { get; init; }

        public string? LastName { get; init; }

        public string? Email { get; init; }

        public string? Phone { get; init; }

        public bool PrefersEmail { get; init; }

        public bool PrefersSms { get; init; }
    }

    public sealed class PropertyBody
    {
        public string? PropertyType { get; init; }

        public string? AddressLine1 { get; init; }

        public string? AddressLine2 { get; init; }

        public string? City { get; init; }

        public string? State { get; init; }

        public string? PostalCode { get; init; }

        public string? AccessInstructions { get; init; }
    }

    public sealed class ServiceBody
    {
        public Guid? CategoryId { get; init; }

        public Guid? ServiceId { get; init; }

        public bool NotSure { get; init; }

        public string? Description { get; init; }

        public string? Urgency { get; init; }

        public bool HasActiveDamage { get; init; }
    }

    public sealed class AvailabilityBody
    {
        public string? DateMode { get; init; }

        public string? PreferredDate { get; init; }

        public string? TimeWindow { get; init; }

        public string? SchedulingNotes { get; init; }
    }
}
