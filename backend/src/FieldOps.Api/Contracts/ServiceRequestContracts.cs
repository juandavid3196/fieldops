using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Api.Contracts;

/// <summary>Body of PUT /service-requests/{id}/assignee; null unassigns (BR-09).</summary>
public sealed record AssignRequestBody(Guid? AssigneeUserId);

/// <summary>Body of PUT /service-requests/{id}/priority (BR-10).</summary>
public sealed record PriorityRequestBody(string? Urgency);

/// <summary>Body of PUT /service-requests/{id}/branch (BR-11).</summary>
public sealed record BranchRequestBody(Guid? BranchId);

/// <summary>Body of notes, information requests and customer responses (BR-12, BR-13).</summary>
public sealed record MessageRequestBody(string? Body);

/// <summary>Body of POST /service-requests/{id}/assessment (BR-15); times are local organization time.</summary>
public sealed record ScheduleAssessmentRequestBody(string? Start, string? End, Guid? TechnicianId, Guid? BranchId);

/// <summary>Body of PUT /service-requests/{id}/assessment (BR-15).</summary>
public sealed record RescheduleAssessmentRequestBody(string? Start, string? End, Guid? TechnicianId);

/// <summary>Body of POST /service-requests/{id}/cancel.</summary>
public sealed record CancelRequestBody(string? Reason);

public sealed record InternalRequestAvailabilityBody(
    string? DateMode,
    string? PreferredDate,
    string? TimeWindow,
    string? SchedulingNotes);

/// <summary>
/// Body of POST /service-requests (BR-18). No organization identifier is accepted; every id is verified
/// against the session organization and branch scope.
/// </summary>
public sealed record CreateInternalRequestBody(
    Guid? CustomerId,
    Guid? ContactId,
    Guid? PropertyId,
    Guid? CategoryId,
    Guid? ServiceId,
    bool NotSure,
    string? Description,
    string? Urgency,
    bool HasActiveDamage,
    InternalRequestAvailabilityBody? Availability)
{
    public InternalRequestText ToText() =>
        new(
            CustomerId,
            ContactId,
            PropertyId,
            CategoryId,
            ServiceId,
            NotSure,
            Description,
            Urgency,
            HasActiveDamage,
            Availability?.DateMode,
            Availability?.PreferredDate,
            Availability?.TimeWindow,
            Availability?.SchedulingNotes);
}
