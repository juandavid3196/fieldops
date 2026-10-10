using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.PortalDashboard;

internal static class PortalQuery
{
    /// <summary>The optional <c>propertyId</c> filter: absent is "All" (no scoping); a value that is not an id is a field error.</summary>
    public static bool TryParseProperty(string? value, out Guid? propertyId, Dictionary<string, string[]> errors)
    {
        propertyId = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out var id) && id != Guid.Empty)
        {
            propertyId = id;

            return true;
        }

        errors["propertyId"] = ["Select one of your properties."];

        return false;
    }
}

/// <summary>GET /portal/dashboard (customer portal BR-17 … BR-26). Absent <c>propertyId</c> means "All".</summary>
public sealed class GetPortalDashboardHandler(IPortalDashboardStore store, TimeProvider timeProvider)
{
    public async Task<PortalOutcome<PortalDashboard>> HandleAsync(PortalScope scope, string? propertyId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!PortalQuery.TryParseProperty(propertyId, out var property, errors))
        {
            return new PortalOutcome<PortalDashboard>.Invalid(errors);
        }

        return await store.GetDashboardAsync(scope, property, timeProvider.GetUtcNow(), cancellationToken) is { } dashboard
            ? new PortalOutcome<PortalDashboard>.Ok(dashboard)
            : new PortalOutcome<PortalDashboard>.NotFound();
    }
}

/// <summary>GET /portal/updates and POST /portal/updates/seen (customer portal BR-24, BR-25).</summary>
public sealed class PortalUpdatesHandler(IPortalDashboardStore store, TimeProvider timeProvider)
{
    public Task<PortalUpdates> ListAsync(PortalScope scope, CancellationToken cancellationToken) =>
        store.ListUpdatesAsync(scope, timeProvider.GetUtcNow(), cancellationToken);

    public Task MarkSeenAsync(PortalScope scope, CancellationToken cancellationToken) =>
        store.MarkUpdatesSeenAsync(scope, timeProvider.GetUtcNow(), cancellationToken);
}

/// <summary>GET /portal/activity (customer portal BR-26).</summary>
public sealed class ListPortalActivityHandler(IPortalDashboardStore store, TimeProvider timeProvider)
{
    public async Task<PortalOutcome<PortalPage<PortalActivityRow>>> HandleAsync(
        PortalScope scope, string? propertyId, string? page, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var propertyOk = PortalQuery.TryParseProperty(propertyId, out var property, errors);

        if (!PortalPaging.TryParse(page, out var pageNumber))
        {
            errors["page"] = ["Enter a page number of 1 or more."];
        }

        if (!propertyOk || errors.Count > 0)
        {
            return new PortalOutcome<PortalPage<PortalActivityRow>>.Invalid(errors);
        }

        return await store.ListActivityAsync(scope, property, pageNumber, timeProvider.GetUtcNow(), cancellationToken) is { } result
            ? new PortalOutcome<PortalPage<PortalActivityRow>>.Ok(result)
            : new PortalOutcome<PortalPage<PortalActivityRow>>.NotFound();
    }
}

/// <summary>The paged lists of requests, quotes and invoices (customer portal BR-27, BR-30, BR-31).</summary>
public sealed class ListPortalCatalogHandler(IPortalCatalogStore store, TimeProvider timeProvider)
{
    public Task<PortalOutcome<PortalPage<PortalRequestRow>>> RequestsAsync(
        PortalScope scope, string? propertyId, string? page, CancellationToken cancellationToken) =>
        RunAsync(propertyId, page, (property, number) => store.ListRequestsAsync(scope, property, number, cancellationToken));

    public Task<PortalOutcome<PortalPage<PortalQuoteRow>>> QuotesAsync(
        PortalScope scope, string? propertyId, string? page, CancellationToken cancellationToken) =>
        RunAsync(propertyId, page, (property, number) => store.ListQuotesAsync(scope, property, number, timeProvider.GetUtcNow(), cancellationToken));

    public Task<PortalOutcome<PortalPage<PortalInvoiceRow>>> InvoicesAsync(
        PortalScope scope, string? propertyId, string? page, CancellationToken cancellationToken) =>
        RunAsync(propertyId, page, (property, number) => store.ListInvoicesAsync(scope, property, number, cancellationToken));

    public async Task<PortalOutcome<PortalRequestDetail>> RequestAsync(PortalScope scope, Guid requestId, CancellationToken cancellationToken) =>
        await store.GetRequestAsync(scope, requestId, cancellationToken) is { } detail
            ? new PortalOutcome<PortalRequestDetail>.Ok(detail)
            : new PortalOutcome<PortalRequestDetail>.NotFound();

    private static async Task<PortalOutcome<PortalPage<T>>> RunAsync<T>(
        string? propertyId, string? page, Func<Guid?, int, Task<PortalPage<T>?>> load)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var propertyOk = PortalQuery.TryParseProperty(propertyId, out var property, errors);

        if (!PortalPaging.TryParse(page, out var pageNumber))
        {
            errors["page"] = ["Enter a page number of 1 or more."];
        }

        if (!propertyOk || errors.Count > 0)
        {
            return new PortalOutcome<PortalPage<T>>.Invalid(errors);
        }

        return await load(property, pageNumber) is { } result
            ? new PortalOutcome<PortalPage<T>>.Ok(result)
            : new PortalOutcome<PortalPage<T>>.NotFound();
    }
}

/// <summary>GET /portal/appointments, the detail and the reschedule request (customer portal BR-22, BR-29, BR-32).</summary>
public sealed class PortalAppointmentsHandler(
    IPortalAppointmentStore store,
    IPortalNotifier notifier,
    IPortalActionThrottle throttle,
    TimeProvider timeProvider)
{
    public const string DateMessage = "Choose a date from tomorrow up to 90 days ahead.";

    public const string WindowMessage = "Select a time window.";

    public const string ReasonRequiredMessage = "Tell us why you need to reschedule.";

    public const string ReasonTooLongMessage = "Reason must be 500 characters or fewer.";

    public const int ReasonMaxLength = 500;

    public async Task<PortalOutcome<PortalPage<PortalAppointment>>> ListAsync(
        PortalScope scope, string? kind, string? propertyId, string? page, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        PortalAppointmentScope scopeKind = PortalAppointmentScope.Upcoming;

        if (!string.IsNullOrWhiteSpace(kind))
        {
            switch (kind.Trim().ToLowerInvariant())
            {
                case "upcoming":
                    break;
                case "past":
                    scopeKind = PortalAppointmentScope.Past;
                    break;
                default:
                    errors["scope"] = ["Select upcoming or past."];
                    break;
            }
        }

        var propertyOk = PortalQuery.TryParseProperty(propertyId, out var property, errors);

        if (!PortalPaging.TryParse(page, out var pageNumber))
        {
            errors["page"] = ["Enter a page number of 1 or more."];
        }

        if (!propertyOk || errors.Count > 0)
        {
            return new PortalOutcome<PortalPage<PortalAppointment>>.Invalid(errors);
        }

        return await store.ListAsync(scope, scopeKind, property, pageNumber, timeProvider.GetUtcNow(), cancellationToken) is { } result
            ? new PortalOutcome<PortalPage<PortalAppointment>>.Ok(result)
            : new PortalOutcome<PortalPage<PortalAppointment>>.NotFound();
    }

    public async Task<PortalOutcome<PortalAppointment>> GetAsync(PortalScope scope, Guid visitId, CancellationToken cancellationToken) =>
        await store.GetAsync(scope, visitId, timeProvider.GetUtcNow(), cancellationToken) is { } appointment
            ? new PortalOutcome<PortalAppointment>.Ok(appointment)
            : new PortalOutcome<PortalAppointment>.NotFound();

    public async Task<PortalOutcome<PortalRescheduleRequested>> RequestRescheduleAsync(
        PortalScope scope, Guid visitId, PortalRescheduleInput input, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        if (throttle.TryAcquire(PortalAction.Reschedule, scope.UserId) is { } retryAfter)
        {
            return new PortalOutcome<PortalRescheduleRequested>.Throttled(retryAfter);
        }

        var now = timeProvider.GetUtcNow();
        var zone = OrganizationTime.FindZone(await store.GetTimezoneAsync(scope, cancellationToken));
        var today = OrganizationTime.LocalDate(now, zone);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        DateOnly preferred = default;

        if (!SubmitPublicServiceRequestValidator.TryParseDate((input.PreferredDate ?? string.Empty).Trim(), out preferred)
            || preferred <= today
            || preferred > today.AddDays(AvailabilityWindowCalculator.MaxDaysAhead))
        {
            errors["preferredDate"] = [DateMessage];
        }

        if (!VisitRescheduleWindows.IsValid(input.TimeWindow))
        {
            errors["timeWindow"] = [WindowMessage];
        }

        var reason = (input.Reason ?? string.Empty).Trim();

        if (reason.Length == 0)
        {
            errors["reason"] = [ReasonRequiredMessage];
        }
        else if (reason.Length > ReasonMaxLength)
        {
            errors["reason"] = [ReasonTooLongMessage];
        }

        // The visit is resolved first so a foreign id is the same 404 whatever the body (customer portal BR-37).
        if (errors.Count > 0)
        {
            return await store.GetAsync(scope, visitId, now, cancellationToken) is null
                ? new PortalOutcome<PortalRescheduleRequested>.NotFound()
                : new PortalOutcome<PortalRescheduleRequested>.Invalid(errors);
        }

        var result = await store.RequestRescheduleAsync(
            scope, visitId, new PortalRescheduleValues(preferred, input.TimeWindow!, reason), clientIp, now, cancellationToken);

        switch (result)
        {
            case PortalRescheduleResult.Created created:
                await notifier.SendRescheduleAsync(created.Email, cancellationToken);

                return new PortalOutcome<PortalRescheduleRequested>.Created(new PortalRescheduleRequested(created.RequestedOn));
            case PortalRescheduleResult.AlreadyRequested:
                return new PortalOutcome<PortalRescheduleRequested>.Conflict(
                    PortalMessages.RescheduleExistsCode, PortalMessages.RescheduleExistsTitle);
            case PortalRescheduleResult.NotEligible:
                return new PortalOutcome<PortalRescheduleRequested>.Conflict(
                    PortalMessages.RescheduleUnavailableCode, PortalMessages.RescheduleUnavailableTitle);
            default:
                return new PortalOutcome<PortalRescheduleRequested>.NotFound();
        }
    }
}

public static class VisitRescheduleWindows
{
    public static bool IsValid(string? window) => window is "morning" or "afternoon" or "evening" or "any";
}

/// <summary>Properties of the portal (customer portal BR-33): add, and edit the name, the instructions and the primary flag only.</summary>
public sealed partial class PortalPropertiesHandler(IPortalPropertyStore store, IPortalActionThrottle throttle, TimeProvider timeProvider)
{
    public const int NameMaxLength = 140;

    public const int AccessInstructionsMaxLength = 1000;

    public Task<IReadOnlyList<PortalProperty>> ListAsync(PortalScope scope, CancellationToken cancellationToken) =>
        store.ListAsync(scope, cancellationToken);

    public async Task<PortalOutcome<PortalProperty>> CreateAsync(
        PortalScope scope, PortalPropertyCreateInput input, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        if (throttle.TryAcquire(PortalAction.PropertyCreate, scope.UserId) is { } retryAfter)
        {
            return new PortalOutcome<PortalProperty>.Throttled(retryAfter);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var name = Required(errors, "name", input.Name, NameMaxLength, PortalPropertyMessages.NameRequired, PortalPropertyMessages.NameTooLong);
        var line1 = Required(errors, "addressLine1", input.AddressLine1, 180);
        var line2 = Optional(errors, "addressLine2", input.AddressLine2, 180);
        var city = Required(errors, "city", input.City, 100);
        var state = (input.StateRegion ?? string.Empty).Trim();
        var postal = (input.PostalCode ?? string.Empty).Trim();
        var instructions = Optional(
            errors, "accessInstructions", input.AccessInstructions, AccessInstructionsMaxLength, PortalPropertyMessages.InstructionsTooLong);

        if (!UsStates.Codes.Contains(state))
        {
            errors["stateRegion"] = [SubmitPublicServiceRequestValidator.StateMessage];
        }

        if (!PostalCodeRegex().IsMatch(postal))
        {
            errors["postalCode"] = [SubmitPublicServiceRequestValidator.PostalCodeMessage];
        }

        if (!string.Equals((input.CountryCode ?? string.Empty).Trim(), "US", StringComparison.Ordinal))
        {
            errors["countryCode"] = [PortalPropertyMessages.CountryMessage];
        }

        if (errors.Count > 0)
        {
            return new PortalOutcome<PortalProperty>.Invalid(errors);
        }

        var created = await store.CreateAsync(
            scope,
            new PortalPropertyCreateInput(name, line1, line2, city, state, postal, "US", instructions),
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return new PortalOutcome<PortalProperty>.Created(created);
    }

    public async Task<PortalOutcome<PortalProperty>> UpdateAsync(
        PortalScope scope, Guid propertyId, PortalPropertyEditInput input, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        string? name = null;
        string? instructions = null;

        if (input.HasName)
        {
            name = Required(errors, "name", input.Name, NameMaxLength, PortalPropertyMessages.NameRequired, PortalPropertyMessages.NameTooLong);
        }

        if (input.HasAccessInstructions)
        {
            instructions = Optional(
                errors, "accessInstructions", input.AccessInstructions, AccessInstructionsMaxLength, PortalPropertyMessages.InstructionsTooLong);
        }

        if (!UpdatedAtValidation.TryParse(input.UpdatedAt, out var updatedAt))
        {
            errors["updatedAt"] = [PortalPropertyMessages.UpdatedAtMessage];
        }

        if (errors.Count > 0)
        {
            return new PortalOutcome<PortalProperty>.Invalid(errors);
        }

        var result = await store.UpdateAsync(
            scope,
            propertyId,
            input with { Name = name, AccessInstructions = instructions },
            updatedAt,
            clientIp,
            timeProvider.GetUtcNow(),
            cancellationToken);

        return result switch
        {
            PortalPropertyEditResult.Saved saved => new PortalOutcome<PortalProperty>.Ok(saved.Property),
            PortalPropertyEditResult.Changed => new PortalOutcome<PortalProperty>.Conflict(
                PortalMessages.PropertyChangedCode, PortalMessages.PropertyChangedTitle),
            _ => new PortalOutcome<PortalProperty>.NotFound(),
        };
    }

    private static string Required(
        Dictionary<string, string[]> errors, string key, string? value, int max, string? requiredMessage = null, string? tooLongMessage = null)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            errors[key] = [requiredMessage ?? FieldRulesValidatorBase<object>.RequiredMessage];
        }
        else if (text.Length > max)
        {
            errors[key] = [tooLongMessage ?? FieldRulesValidatorBase<object>.TooLongMessage(max)];
        }

        return text;
    }

    private static string? Optional(Dictionary<string, string[]> errors, string key, string? value, int max, string? tooLongMessage = null)
    {
        var text = (value ?? string.Empty).Trim();

        if (text.Length > max)
        {
            errors[key] = [tooLongMessage ?? FieldRulesValidatorBase<object>.TooLongMessage(max)];
        }

        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("^[0-9]{5}(-[0-9]{4})?$")]
    private static partial Regex PostalCodeRegex();
}

/// <summary>POST /portal/messages (customer portal BR-35): audit without text, then the email after the commit.</summary>
public sealed class SendPortalMessageHandler(IPortalMessageStore store, IPortalNotifier notifier, IPortalActionThrottle throttle)
{
    public const int MessageMaxLength = 1000;

    public const string RequiredMessage = "Enter your message.";

    public const string TooLongMessage = "Message must be 1,000 characters or fewer.";

    public async Task<PortalOutcome<bool>> HandleAsync(
        PortalScope scope, string? message, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var target = await store.GetTargetAsync(scope, cancellationToken);

        // Not configured: nothing can be sent, whatever the body is.
        if (string.IsNullOrWhiteSpace(target.RecipientEmail))
        {
            return new PortalOutcome<bool>.Conflict(PortalMessages.MessagingUnavailableCode, PortalMessages.MessagingUnavailableTitle);
        }

        if (throttle.TryAcquire(PortalAction.Message, scope.UserId) is { } retryAfter)
        {
            return new PortalOutcome<bool>.Throttled(retryAfter);
        }

        var text = (message ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return PortalOutcome<bool>.Failure("message", RequiredMessage);
        }

        if (text.Length > MessageMaxLength)
        {
            return PortalOutcome<bool>.Failure("message", TooLongMessage);
        }

        await store.WriteMessageAuditAsync(scope, text.Length, clientIp, cancellationToken);
        await notifier.SendMessageAsync(target, text, target.OrganizationName, cancellationToken);

        return new PortalOutcome<bool>.Ok(true);
    }
}

/// <summary>Composes the emails to the organization (customer portal BR-32, BR-35); plain text, no templates (AS-02).</summary>
public static class PortalEmailComposer
{
    public static EmailMessage Message(PortalMessageTarget target, string message)
    {
        var text =
            $"Message from {target.CustomerName}\n\n"
            + $"Contact: {target.ContactName}\n"
            + $"Email: {target.ContactEmail}\n"
            + $"Phone: {target.ContactPhone}\n\n"
            + message
            + "\n";

        return new EmailMessage(
            target.RecipientEmail!,
            $"Message from {target.CustomerName}",
            text,
            Html(text),
            string.IsNullOrWhiteSpace(target.ContactEmail) ? null : target.ContactEmail.Trim());
    }

    public static EmailMessage Reschedule(PortalRescheduleEmailData data)
    {
        var text =
            $"{data.CustomerName} asked to reschedule {data.WorkOrderNumber}.\n\n"
            + $"Current: {data.CurrentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({data.CurrentWindow})\n"
            + $"Preferred: {data.PreferredDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({data.PreferredWindow})\n\n"
            + $"Reason: {data.Reason}\n";

        return new EmailMessage(data.RecipientEmail!, $"Reschedule requested for {data.WorkOrderNumber}", text, Html(text));
    }

    private static string Html(string text) =>
        "<!DOCTYPE html><html><body style=\"font-family:Arial,sans-serif;color:#1f2937\"><p>"
        + WebUtility.HtmlEncode(text).Replace("\n", "<br/>", StringComparison.Ordinal)
        + "</p></body></html>";
}

/// <summary>GET /portal/work-orders/{id}/completion-report (customer portal BR-31): the same PDF as the public report.</summary>
public sealed class GetPortalWorkOrderReportHandler(IPortalCatalogStore store, ICompletionReportPdfRenderer renderer)
{
    public async Task<DocumentFile?> HandleAsync(PortalScope scope, Guid workOrderId, CancellationToken cancellationToken) =>
        await store.GetWorkOrderReportAsync(scope, workOrderId, cancellationToken) is { } source
            ? new DocumentFile(
                CompletionReportPdfDocumentComposer.FileName(source),
                OnlinePaymentMessages.ReceiptContentType,
                renderer.Render(CompletionReportPdfDocumentComposer.Compose(source)))
            : null;
}
