using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Application.Features.ServiceRequests;
using FluentValidation;

namespace FieldOps.Application.Features.PortalRequests;

/// <summary>The contact of the session, used as the guest snapshot of a portal request (customer portal BR-28).</summary>
public sealed record PortalContactSnapshot(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms);

public sealed record PortalPropertyChoice(Guid? PropertyId, PublicPropertyInput? NewProperty);

public sealed record SubmitPortalServiceRequestCommand(
    PortalPropertyChoice Property,
    PublicServiceInput Service,
    PublicAvailabilityInput Availability,
    bool Consent,
    string? Website,
    IReadOnlyList<PublicAttachmentInput> Attachments);

public sealed record PortalRequestCreated(Guid RequestId, string RequestNumber);

public interface IPortalRequestStore
{
    Task<PortalContactSnapshot> GetContactAsync(PortalScope scope, CancellationToken cancellationToken);

    Task<bool> IsActivePropertyAsync(PortalScope scope, Guid propertyId, CancellationToken cancellationToken);
}

/// <summary>GET /portal/service-request-form: the public form configuration of the session organization (customer portal BR-28).</summary>
public sealed class GetPortalServiceRequestFormHandler(IPublicServiceRequestStore store)
{
    public Task<PublicServiceRequestForm?> HandleAsync(PortalScope scope, CancellationToken cancellationToken) =>
        store.FindAcceptingFormByOrganizationAsync(scope.OrganizationId, cancellationToken);
}

/// <summary>
/// POST /portal/service-requests (customer portal BR-28): the public field rules, the customer and contact of the session,
/// an existing property of the customer or a new one. The confirmation email goes out after the commit (public BR-19).
/// </summary>
public sealed class SubmitPortalServiceRequestHandler(
    IPublicServiceRequestStore publicStore,
    IPortalRequestStore portalStore,
    IValidator<PublicSubmissionValidationInput> validator,
    IPublicRequestConfirmationSender confirmationSender,
    IPortalActionThrottle throttle,
    TimeProvider timeProvider)
{
    public const string PropertyKey = "property.propertyId";

    public const string PropertyMessage = "Select one of your properties.";

    // Valid stand-ins for the parts of the public rules this flow does not take from the client.
    private static readonly PublicContactInput PlaceholderContact = new("Portal", "Contact", "contact@example.com", "5555550100", true, false);

    private static readonly PublicPropertyInput PlaceholderProperty = new("home", "1 Main St", null, "City", "CA", "90210", null);

    public async Task<PortalOutcome<PortalRequestCreated>> HandleAsync(
        PortalScope scope, SubmitPortalServiceRequestCommand command, CancellationToken cancellationToken)
    {
        var form = await publicStore.FindAcceptingFormByOrganizationAsync(scope.OrganizationId, cancellationToken);

        if (form is null)
        {
            return new PortalOutcome<PortalRequestCreated>.NotFound();
        }

        // Honeypot (public BR-17): the generic failure before any other rule.
        if (!string.IsNullOrEmpty(command.Website))
        {
            return new PortalOutcome<PortalRequestCreated>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal));
        }

        if (throttle.TryAcquire(PortalAction.ServiceRequest, scope.UserId) is { } retryAfter)
        {
            return new PortalOutcome<PortalRequestCreated>.Throttled(retryAfter);
        }

        var now = timeProvider.GetUtcNow();
        var zone = OrganizationTime.FindZone(form.Timezone);
        var today = AvailabilityWindowCalculator.Today(now, zone);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var choice = command.Property;
        var existing = choice.PropertyId is { } id && choice.NewProperty is null ? id : (Guid?)null;
        var newProperty = existing is null ? choice.NewProperty : null;

        if (existing is null && newProperty is null)
        {
            errors[PropertyKey] = [PropertyMessage];
        }
        else if (existing is { } propertyId && !await portalStore.IsActivePropertyAsync(scope, propertyId, cancellationToken))
        {
            errors[PropertyKey] = [PropertyMessage];
        }

        var contact = await portalStore.GetContactAsync(scope, cancellationToken);
        var validated = new SubmitPublicServiceRequestCommand(
            string.Empty,
            PlaceholderContact,
            newProperty ?? PlaceholderProperty,
            command.Service,
            command.Availability,
            command.Consent,
            null,
            command.Attachments);
        var validation = await validator.ValidateAsync(new PublicSubmissionValidationInput(validated, today, form), cancellationToken);

        foreach (var group in validation.Errors.GroupBy(error => error.PropertyName, StringComparer.Ordinal))
        {
            errors[group.Key] = group.Select(error => error.ErrorMessage).Distinct().ToArray();
        }

        if (errors.Count > 0)
        {
            return new PortalOutcome<PortalRequestCreated>.Invalid(errors);
        }

        var withContact = validated with
        {
            Contact = new PublicContactInput(
                contact.FirstName,
                contact.LastName,
                contact.Email ?? string.Empty,
                contact.Phone ?? string.Empty,
                contact.PrefersEmail,
                contact.PrefersSms),
        };
        var submission = SubmitPublicServiceRequestHandler.BuildSubmission(withContact, form, zone, now) with
        {
            Portal = new PortalSubmissionContext(scope.CustomerId, scope.ContactId, scope.UserId, existing),
        };

        var outcome = await publicStore.SubmitAsync(submission, cancellationToken);

        if (outcome is PublicSubmissionOutcome.PropertyUnavailable)
        {
            return PortalOutcome<PortalRequestCreated>.Failure(PropertyKey, PropertyMessage);
        }

        if (outcome is not PublicSubmissionOutcome.Created created)
        {
            return new PortalOutcome<PortalRequestCreated>.Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["service.categoryId"] = [SubmitPublicServiceRequestValidator.CategoryMessage],
                ["service.serviceId"] = [SubmitPublicServiceRequestValidator.ServiceMessage],
            });
        }

        if (!string.IsNullOrWhiteSpace(contact.Email))
        {
            await confirmationSender.SendAsync(
                new PublicRequestConfirmation(created.RequestId, contact.Email.Trim(), contact.FirstName, form.OrganizationName, created.RequestNumber),
                cancellationToken);
        }

        return new PortalOutcome<PortalRequestCreated>.Created(new PortalRequestCreated(created.RequestId, created.RequestNumber));
    }
}
