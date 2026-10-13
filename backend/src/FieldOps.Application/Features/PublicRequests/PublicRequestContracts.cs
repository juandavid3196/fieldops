namespace FieldOps.Application.Features.PublicRequests;

public sealed record PublicFormService(Guid Id, string Name);

public sealed record PublicFormCategory(Guid Id, string Name, IReadOnlyList<PublicFormService> Services);

/// <summary>
/// Form configuration of an organization that accepts public requests
/// (BR-01). Only active categories with at least one active service; no
/// price, cost or tax data.
/// </summary>
public sealed record PublicServiceRequestForm(
    Guid OrganizationId,
    Guid MainBranchId,
    string OrganizationName,
    string? Phone,
    string? Website,
    string RequestPrefix,
    string Timezone,
    IReadOnlyList<PublicFormCategory> Categories);

public sealed record PublicContactInput(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms);

public sealed record PublicPropertyInput(
    string? PropertyType,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? AccessInstructions);

public sealed record PublicServiceInput(
    Guid? CategoryId,
    Guid? ServiceId,
    bool NotSure,
    string? Description,
    string? Urgency,
    bool HasActiveDamage);

public sealed record PublicAvailabilityInput(
    string? DateMode,
    string? PreferredDate,
    string? TimeWindow,
    string? SchedulingNotes);

public sealed record PublicAttachmentInput(string? FileName, byte[] Content);

public sealed record SubmitPublicServiceRequestCommand(
    string Slug,
    PublicContactInput Contact,
    PublicPropertyInput Property,
    PublicServiceInput Service,
    PublicAvailabilityInput Availability,
    bool Consent,
    string? Website,
    IReadOnlyList<PublicAttachmentInput> Attachments);

/// <summary>A submission together with the server-side context its rules depend on.</summary>
public sealed record PublicSubmissionValidationInput(
    SubmitPublicServiceRequestCommand Command,
    DateOnly Today,
    PublicServiceRequestForm Form);

public sealed record PublicSubmissionAttachment(string FileName, string MimeType, byte[] Content);

/// <summary>Validated, normalized data handed to the store for the single transaction.</summary>
public sealed record PublicSubmission(
    Guid OrganizationId,
    Guid MainBranchId,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    bool PrefersEmail,
    bool PrefersSms,
    string PropertyType,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string? AccessInstructions,
    Guid CategoryId,
    Guid? ServiceId,
    string Description,
    string Urgency,
    bool HasActiveDamage,
    string AvailabilityPreferencesJson,
    DateTimeOffset? PreferredStart,
    DateTimeOffset? PreferredEnd,
    DateTimeOffset ConsentAt,
    IReadOnlyList<PublicSubmissionAttachment> Attachments,
    PortalSubmissionContext? Portal = null);

/// <summary>
/// Set when the request comes from the customer portal (customer portal BR-28): the customer, contact and user come from the
/// session and the property is one of the customer's (<paramref name="ExistingPropertyId"/>) or a new one built from the submission.
/// </summary>
public sealed record PortalSubmissionContext(Guid CustomerId, Guid ContactId, Guid UserId, Guid? ExistingPropertyId);

public abstract record PublicSubmissionOutcome
{
    private PublicSubmissionOutcome()
    {
    }

    public sealed record Created(Guid RequestId, string RequestNumber) : PublicSubmissionOutcome;

    /// <summary>The category or service stopped being valid between validation and the transaction.</summary>
    public sealed record CatalogChanged : PublicSubmissionOutcome;

    /// <summary>Portal only: the chosen property stopped being an active property of the customer.</summary>
    public sealed record PropertyUnavailable : PublicSubmissionOutcome;
}

public abstract record SubmitPublicServiceRequestResult
{
    private SubmitPublicServiceRequestResult()
    {
    }

    /// <summary>Unknown slug or an organization that does not accept public requests (BR-01).</summary>
    public sealed record NotFound : SubmitPublicServiceRequestResult;

    /// <summary>The honeypot field was filled (BR-17).</summary>
    public sealed record Honeypot : SubmitPublicServiceRequestResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SubmitPublicServiceRequestResult;

    public sealed record Created(string RequestNumber) : SubmitPublicServiceRequestResult;
}

public sealed record PublicRequestConfirmation(
    Guid RequestId,
    string RecipientEmail,
    string FirstName,
    string OrganizationName,
    string RequestNumber);

/// <summary>
/// Sends the confirmation email after the commit (BR-19). Implementations
/// never throw for a delivery failure and never log personal data.
/// </summary>
public interface IPublicRequestConfirmationSender
{
    Task SendAsync(PublicRequestConfirmation confirmation, CancellationToken cancellationToken);
}

public interface IPublicServiceRequestStore
{
    /// <summary>The form configuration when the slug (lowercase) accepts public requests, otherwise null (BR-01).</summary>
    Task<PublicServiceRequestForm?> FindAcceptingFormAsync(string slug, CancellationToken cancellationToken);

    /// <summary>The same form configuration for the organization of a portal session (customer portal BR-28).</summary>
    Task<PublicServiceRequestForm?> FindAcceptingFormByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// One transaction: lock the organization, resolve contact/customer
    /// (BR-09), number, create all rows (BR-10..BR-15). Any failure persists nothing.
    /// </summary>
    Task<PublicSubmissionOutcome> SubmitAsync(PublicSubmission submission, CancellationToken cancellationToken);
}
