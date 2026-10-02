using System.Globalization;
using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.PublicRequests;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Raw pipeline query values (BR-06) before parsing.</summary>
public sealed record PipelineQueryText(
    string? AssigneeUserId,
    string? CategoryId,
    string? Urgency,
    string? Source,
    string? Created,
    string? Search,
    string? Status,
    int? Offset);

public sealed record InternalRequestText(
    Guid? CustomerId,
    Guid? ContactId,
    Guid? PropertyId,
    Guid? CategoryId,
    Guid? ServiceId,
    bool NotSure,
    string? Description,
    string? Urgency,
    bool HasActiveDamage,
    string? DateMode,
    string? PreferredDate,
    string? TimeWindow,
    string? SchedulingNotes);

public sealed record UploadedFile(string? FileName, byte[] Content);

public sealed class ListServiceRequestPipelineHandler(
    IServiceRequestStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    private static readonly string[] Urgencies = ["standard", "urgent", "emergency"];

    private static readonly string[] Sources = ["public_form", "internal"];

    private static readonly string[] CreatedRanges = ["today", "7d", "30d"];

    public async Task<ServiceRequestResult<PipelineView>> HandleAsync(
        Guid organizationId, Guid membershipId, PipelineQueryText query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        Guid? assignee = null;
        var unassigned = false;

        if (!string.IsNullOrWhiteSpace(query.AssigneeUserId))
        {
            if (string.Equals(query.AssigneeUserId.Trim(), "unassigned", StringComparison.Ordinal))
            {
                unassigned = true;
            }
            else if (Guid.TryParse(query.AssigneeUserId, out var parsed))
            {
                assignee = parsed;
            }
            else
            {
                errors["assigneeUserId"] = [ServiceRequestMessages.FilterInvalid];
            }
        }

        Guid? category = null;

        if (!string.IsNullOrWhiteSpace(query.CategoryId))
        {
            if (Guid.TryParse(query.CategoryId, out var parsed))
            {
                category = parsed;
            }
            else
            {
                errors["categoryId"] = [ServiceRequestMessages.FilterInvalid];
            }
        }

        var urgency = OptionalChoice(query.Urgency, Urgencies, "urgency", errors);
        var source = OptionalChoice(query.Source, Sources, "source", errors);
        var created = OptionalChoice(query.Created, CreatedRanges, "created", errors);

        var search = query.Search?.Trim();

        if (search is { Length: > ServiceRequestMessages.SearchMaxLength })
        {
            errors["search"] = [ServiceRequestMessages.SearchTooLong];
        }

        string? status = null;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (RequestTransitions.TryParseCode(query.Status.Trim(), out var parsedStatus)
                && RequestTransitions.IsOpen(parsedStatus))
            {
                status = RequestTransitions.Code(parsedStatus);
            }
            else
            {
                errors["status"] = [ServiceRequestMessages.FilterInvalid];
            }
        }

        if (query.Offset is < 0 || (query.Offset is not null && status is null))
        {
            errors["offset"] = [ServiceRequestMessages.FilterInvalid];
        }

        if (errors.Count > 0)
        {
            return ServiceRequestResult<PipelineView>.Invalid(errors);
        }

        var filter = new PipelineFilter(
            assignee, unassigned, category, urgency, source, created, string.IsNullOrEmpty(search) ? null : search);

        var idErrors = await store.ValidateFilterIdsAsync(organizationId, filter, cancellationToken);

        if (idErrors.Count > 0)
        {
            return ServiceRequestResult<PipelineView>.Invalid(idErrors);
        }

        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return ServiceRequestResult<PipelineView>.Ok(await store.GetPipelineAsync(
            organizationId,
            scope,
            filter,
            new PipelinePage(status, query.Offset ?? 0),
            timeProvider.GetUtcNow(),
            cancellationToken));
    }

    private static string? OptionalChoice(
        string? value, string[] allowed, string key, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (!allowed.Contains(trimmed, StringComparer.Ordinal))
        {
            errors[key] = [ServiceRequestMessages.FilterInvalid];

            return null;
        }

        return trimmed;
    }
}

public sealed class GetServiceRequestMetricsHandler(
    IServiceRequestStore store, IBranchScopeResolver scopes, TimeProvider timeProvider)
{
    public async Task<RequestMetrics> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetMetricsAsync(organizationId, scope, timeProvider.GetUtcNow(), cancellationToken);
    }
}

public sealed class GetServiceRequestOptionsHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public async Task<RequestOptions> HandleAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetOptionsAsync(organizationId, scope, cancellationToken);
    }
}

public sealed class GetRequestCustomerOptionsHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public async Task<ServiceRequestResult<CustomerOptions>> HandleAsync(
        Guid organizationId, Guid membershipId, string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim();

        if (term is { Length: > ServiceRequestMessages.SearchMaxLength })
        {
            return ServiceRequestResult<CustomerOptions>.Invalid("search", ServiceRequestMessages.SearchTooLong);
        }

        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return ServiceRequestResult<CustomerOptions>.Ok(
            await store.GetCustomerOptionsAsync(organizationId, scope, term, cancellationToken));
    }
}

public sealed class GetServiceRequestHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public async Task<RequestDetail?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid requestId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetDetailAsync(organizationId, scope, requestId, cancellationToken);
    }
}

public sealed class GetRequestAttachmentHandler(IServiceRequestStore store, IBranchScopeResolver scopes)
{
    public async Task<AttachmentDownload?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid requestId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        return await store.GetAttachmentAsync(organizationId, scope, requestId, attachmentId, cancellationToken);
    }
}

/// <summary>
/// Every mutation of one request (BR-03, BR-09 to BR-16): shape validation here, tenant, scope, status
/// and eligibility checks inside the store's locked transaction. The information-request email is sent
/// only after the commit and never fails the response (BR-13).
/// </summary>
public sealed class ServiceRequestActionHandler(
    IServiceRequestStore store,
    IBranchScopeResolver scopes,
    IRequestInformationNotifier notifier,
    TimeProvider timeProvider)
{
    public const string DefaultAttachmentError = "Attach at least one file.";

    public Task<ServiceRequestResult<RequestDetail>> StartReviewAsync(
        MembershipCall call, Guid id, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.StartReview(), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> AssignAsync(
        MembershipCall call, Guid id, Guid? assigneeUserId, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.Assign(assigneeUserId), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> ChangePriorityAsync(
        MembershipCall call, Guid id, string? urgency, CancellationToken cancellationToken)
    {
        var value = urgency?.Trim();

        return value is "standard" or "urgent" or "emergency"
            ? RunAsync(call, id, new RequestMutation.ChangePriority(value), cancellationToken)
            : Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid("urgency", ServiceRequestMessages.UrgencyInvalid));
    }

    public Task<ServiceRequestResult<RequestDetail>> SetBranchAsync(
        MembershipCall call, Guid id, Guid? branchId, CancellationToken cancellationToken) =>
        branchId is { } branch
            ? RunAsync(call, id, new RequestMutation.SetBranch(branch), cancellationToken)
            : Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid("branchId", ServiceRequestMessages.BranchNotAllowed));

    public Task<ServiceRequestResult<RequestDetail>> AddNoteAsync(
        MembershipCall call, Guid id, string? body, CancellationToken cancellationToken) =>
        WithBody(body, text => RunAsync(call, id, new RequestMutation.AddNote(text), cancellationToken));

    public Task<ServiceRequestResult<RequestDetail>> RequestInformationAsync(
        MembershipCall call, Guid id, string? body, CancellationToken cancellationToken) =>
        WithBody(body, text => RunAsync(call, id, new RequestMutation.RequestInformation(text), cancellationToken));

    public Task<ServiceRequestResult<RequestDetail>> LogResponseAsync(
        MembershipCall call, Guid id, string? body, CancellationToken cancellationToken) =>
        WithBody(body, text => RunAsync(call, id, new RequestMutation.LogResponse(text), cancellationToken));

    public async Task<ServiceRequestResult<RequestDetail>> ScheduleAssessmentAsync(
        MembershipCall call,
        Guid id,
        string? start,
        string? end,
        Guid? technicianId,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        var slot = await ResolveSlotAsync(call.OrganizationId, start, end, cancellationToken);

        return slot.Errors is not null
            ? ServiceRequestResult<RequestDetail>.Invalid(slot.Errors)
            : await RunAsync(
                call,
                id,
                new RequestMutation.ScheduleAssessment(slot.Start, slot.End, technicianId, branchId),
                cancellationToken);
    }

    public async Task<ServiceRequestResult<RequestDetail>> RescheduleAssessmentAsync(
        MembershipCall call,
        Guid id,
        string? start,
        string? end,
        Guid? technicianId,
        CancellationToken cancellationToken)
    {
        var slot = await ResolveSlotAsync(call.OrganizationId, start, end, cancellationToken);

        return slot.Errors is not null
            ? ServiceRequestResult<RequestDetail>.Invalid(slot.Errors)
            : await RunAsync(
                call,
                id,
                new RequestMutation.RescheduleAssessment(slot.Start, slot.End, technicianId),
                cancellationToken);
    }

    public Task<ServiceRequestResult<RequestDetail>> CancelAssessmentAsync(
        MembershipCall call, Guid id, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.CancelAssessment(), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> CompleteAssessmentAsync(
        MembershipCall call, Guid id, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.CompleteAssessment(), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> MarkReadyForQuoteAsync(
        MembershipCall call, Guid id, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.MarkReadyForQuote(), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> MoveToReviewAsync(
        MembershipCall call, Guid id, CancellationToken cancellationToken) =>
        RunAsync(call, id, new RequestMutation.MoveToReview(), cancellationToken);

    public Task<ServiceRequestResult<RequestDetail>> CancelRequestAsync(
        MembershipCall call, Guid id, string? reason, CancellationToken cancellationToken)
    {
        var text = reason?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid("reason", ServiceRequestMessages.ReasonRequired));
        }

        return text.Length > ServiceRequestMessages.ReasonMaxLength
            ? Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid(
                "reason", ServiceRequestMessages.TooLong(ServiceRequestMessages.ReasonMaxLength)))
            : RunAsync(call, id, new RequestMutation.CancelRequest(text), cancellationToken);
    }

    public Task<ServiceRequestResult<RequestDetail>> AddAttachmentsAsync(
        MembershipCall call, Guid id, IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (files.Count == 0)
        {
            errors["attachments"] = [DefaultAttachmentError];
        }
        else if (files.Count > AttachmentContentInspector.MaxFiles)
        {
            errors["attachments"] = [ServiceRequestMessages.TooManyAttachments];
        }
        else
        {
            long total = 0;

            for (var index = 0; index < files.Count; index++)
            {
                total += files[index].Content.Length;
                var inspection = AttachmentContentInspector.Inspect(files[index].FileName, files[index].Content);

                if (!inspection.IsValid)
                {
                    errors[string.Create(CultureInfo.InvariantCulture, $"attachments[{index}]")] = [inspection.Error!];
                }
            }

            if (total > AttachmentContentInspector.MaxTotalBytes)
            {
                errors["attachments"] = [SubmitPublicServiceRequestValidator.TotalSizeMessage];
            }
        }

        if (errors.Count > 0)
        {
            return Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid(errors));
        }

        var inputs = files
            .Select(file => new AttachmentInput(
                AttachmentContentInspector.SanitizeFileName(file.FileName),
                AttachmentContentInspector.Inspect(file.FileName, file.Content).MimeType!,
                file.Content))
            .ToList();

        return RunAsync(call, id, new RequestMutation.AddAttachments(inputs), cancellationToken);
    }

    private Task<ServiceRequestResult<RequestDetail>> WithBody(
        string? body, Func<string, Task<ServiceRequestResult<RequestDetail>>> run)
    {
        var text = body?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid("body", ServiceRequestMessages.BodyRequired));
        }

        return text.Length > ServiceRequestMessages.BodyMaxLength
            ? Task.FromResult(ServiceRequestResult<RequestDetail>.Invalid(
                "body", ServiceRequestMessages.TooLong(ServiceRequestMessages.BodyMaxLength)))
            : run(text);
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End, Dictionary<string, string[]>? Errors)> ResolveSlotAsync(
        Guid organizationId, string? start, string? end, CancellationToken cancellationToken)
    {
        var organization = await store.GetOrganizationContextAsync(organizationId, cancellationToken);
        var zone = OrganizationTime.FindZone(organization?.Timezone);

        return AssessmentSlotRules.TryResolve(start, end, zone, timeProvider.GetUtcNow(), out var from, out var to, out var errors)
            ? (from, to, null)
            : (default, default, errors);
    }

    private async Task<ServiceRequestResult<RequestDetail>> RunAsync(
        MembershipCall call, Guid id, RequestMutation mutation, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var actor = new RequestActor(call.OrganizationId, call.UserId, scope, call.IpAddress);

        var outcome = await store.MutateAsync(actor, id, mutation, cancellationToken);

        switch (outcome)
        {
            case RequestMutationOutcome.Succeeded succeeded:
                if (succeeded.Email is { } email)
                {
                    // After the commit; the notifier never throws for a delivery failure.
                    await notifier.SendAsync(email, cancellationToken);
                }

                return ServiceRequestResult<RequestDetail>.Ok(succeeded.Detail);

            case RequestMutationOutcome.Invalid invalid:
                return ServiceRequestResult<RequestDetail>.Invalid(invalid.Errors);

            case RequestMutationOutcome.Conflict conflict:
                return ServiceRequestResult<RequestDetail>.Conflict(conflict.Message);

            default:
                return ServiceRequestResult<RequestDetail>.NotFound();
        }
    }
}

/// <summary>The session of a call: organization, membership, user and client address.</summary>
public sealed record MembershipCall(Guid OrganizationId, Guid MembershipId, Guid UserId, IPAddress? IpAddress);

/// <summary>Validates and creates an internal request for an existing customer (BR-18, FR-15).</summary>
public sealed class CreateInternalRequestHandler(
    IServiceRequestStore store,
    IBranchScopeResolver scopes,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceRequestResult<RequestDetail>> HandleAsync(
        MembershipCall call, InternalRequestText input, CancellationToken cancellationToken)
    {
        var organization = await store.GetOrganizationContextAsync(call.OrganizationId, cancellationToken);

        if (organization is null)
        {
            return ServiceRequestResult<RequestDetail>.NotFound();
        }

        var zone = OrganizationTime.FindZone(organization.Timezone);
        var today = OrganizationTime.LocalDate(timeProvider.GetUtcNow(), zone);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        Require(input.CustomerId, "customerId", ServiceRequestMessages.CustomerNotAllowed, errors);
        Require(input.ContactId, "contactId", ServiceRequestMessages.ContactNotAllowed, errors);
        Require(input.PropertyId, "propertyId", ServiceRequestMessages.PropertyNotAllowed, errors);

        if (input.CategoryId is null)
        {
            errors["categoryId"] = [SubmitPublicServiceRequestValidator.CategoryMessage];
        }

        if (input.NotSure ? input.ServiceId is not null : input.ServiceId is null)
        {
            errors["serviceId"] = [SubmitPublicServiceRequestValidator.ServiceMessage];
        }

        var description = input.Description?.Trim() ?? string.Empty;

        if (description.Length == 0)
        {
            errors["description"] = [ServiceRequestMessages.BodyRequired];
        }
        else if (description.Length > 1000)
        {
            errors["description"] = [ServiceRequestMessages.TooLong(1000)];
        }

        if (input.Urgency is not ("standard" or "urgent" or "emergency"))
        {
            errors["urgency"] = [SubmitPublicServiceRequestValidator.UrgencyMessage];
        }

        var (preferredStart, preferredEnd, preferredDate) = ValidateAvailability(input, today, zone, errors);

        if (errors.Count > 0)
        {
            return ServiceRequestResult<RequestDetail>.Invalid(errors);
        }

        var notes = string.IsNullOrWhiteSpace(input.SchedulingNotes) ? null : input.SchedulingNotes.Trim();
        var availabilityJson = JsonSerializer.Serialize(
            new
            {
                dateMode = input.DateMode,
                preferredDate,
                timeWindow = input.TimeWindow,
                schedulingNotes = notes,
            },
            JsonOptions);

        var scope = await scopes.ResolveAsync(call.OrganizationId, call.MembershipId, cancellationToken);
        var outcome = await store.CreateInternalAsync(
            new RequestActor(call.OrganizationId, call.UserId, scope, call.IpAddress),
            new InternalRequestInput(
                input.CustomerId!.Value,
                input.ContactId!.Value,
                input.PropertyId!.Value,
                input.CategoryId!.Value,
                input.NotSure ? null : input.ServiceId,
                description,
                input.Urgency!,
                input.HasActiveDamage,
                availabilityJson,
                preferredStart,
                preferredEnd),
            cancellationToken);

        return outcome switch
        {
            RequestCreationOutcome.Created created => ServiceRequestResult<RequestDetail>.Ok(created.Detail),
            RequestCreationOutcome.Invalid invalid => ServiceRequestResult<RequestDetail>.Invalid(invalid.Errors),
            _ => throw new InvalidOperationException("Unknown creation outcome."),
        };
    }

    private static void Require(Guid? value, string key, string message, Dictionary<string, string[]> errors)
    {
        if (value is null || value == Guid.Empty)
        {
            errors[key] = [message];
        }
    }

    // Public service request BR-06/BR-07: the same date modes, window, 90-day range and notes limit.
    private static (DateTimeOffset? Start, DateTimeOffset? End, string? PreferredDate) ValidateAvailability(
        InternalRequestText input, DateOnly today, TimeZoneInfo zone, Dictionary<string, string[]> errors)
    {
        const string dateKey = "availability.preferredDate";

        if (input.DateMode is not ("asap" or "date" or "flexible"))
        {
            errors["availability.dateMode"] = [SubmitPublicServiceRequestValidator.DateModeMessage];
        }

        if (!AvailabilityWindowCalculator.IsValidWindow(input.TimeWindow))
        {
            errors["availability.timeWindow"] = [SubmitPublicServiceRequestValidator.TimeWindowMessage];
        }

        if (input.SchedulingNotes?.Trim().Length > 1000)
        {
            errors["availability.schedulingNotes"] = [ServiceRequestMessages.TooLong(1000)];
        }

        var raw = input.PreferredDate?.Trim();

        if (input.DateMode != "date")
        {
            if (!string.IsNullOrEmpty(raw))
            {
                errors[dateKey] = [SubmitPublicServiceRequestValidator.PreferredDateNotAllowedMessage];
            }

            return (null, null, null);
        }

        if (string.IsNullOrEmpty(raw))
        {
            errors[dateKey] = [SubmitPublicServiceRequestValidator.PreferredDateRequiredMessage];

            return (null, null, null);
        }

        if (!SubmitPublicServiceRequestValidator.TryParseDate(raw, out var date)
            || date < today
            || date > today.AddDays(AvailabilityWindowCalculator.MaxDaysAhead))
        {
            errors[dateKey] = [SubmitPublicServiceRequestValidator.PreferredDateRangeMessage];

            return (null, null, null);
        }

        if (!errors.ContainsKey("availability.timeWindow"))
        {
            var (start, end) = AvailabilityWindowCalculator.Calculate(date, input.TimeWindow!, zone);

            return (start, end, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return (null, null, null);
    }
}
