namespace FieldOps.Api.Contracts;

/// <summary>
/// Bodies of the customer portal endpoints. None carries an organization, customer, contact (but the account switch), quote or
/// invoice identifier of its own, or a token: the resource id travels in the path and unknown properties are ignored.
/// </summary>
public sealed record PortalSwitchAccountRequest(Guid? ContactId);

public sealed record PortalAcceptInvitationRequest(string? Token, string? Password, string? LastName);

public sealed record PortalAcceptExistingInvitationRequest(string? Token, string? Password);

public sealed record PortalQuoteSelectionRequest(List<string?>? SelectedOptionalLineIds);

public sealed record PortalQuoteApproveRequest(List<string?>? SelectedOptionalLineIds, bool? AcceptTerms);

public sealed record PortalQuoteDeclineRequest(string? Reason);

public sealed record PortalQuoteQuestionRequest(string? Message);

public sealed record PortalKeyRequest(string? IdempotencyKey);

public sealed record PortalReviewRequest(decimal? Rating, string? Comment);

public sealed record PortalRescheduleRequestBody(string? PreferredDate, string? TimeWindow, string? Reason);

public sealed record PortalPropertyCreateRequest(
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? AccessInstructions);

public sealed record PortalMessageRequest(string? Message);

public sealed record PortalServiceRequestCreatedResponse(Guid RequestId, string RequestNumber);

/// <summary>
/// JSON of the <c>request</c> multipart part of POST /portal/service-requests (customer portal BR-28): the property is either
/// <c>{ propertyId }</c> or <c>{ newProperty }</c>; the contact and the customer come from the session.
/// </summary>
public sealed class PortalServiceRequestBody
{
    public PortalPropertyBody? Property { get; init; }

    public PublicServiceRequestBody.ServiceBody? Service { get; init; }

    public PublicServiceRequestBody.AvailabilityBody? Availability { get; init; }

    public bool Consent { get; init; }

    public string? Website { get; init; }

    public sealed class PortalPropertyBody
    {
        public Guid? PropertyId { get; init; }

        public PublicServiceRequestBody.PropertyBody? NewProperty { get; init; }
    }
}
