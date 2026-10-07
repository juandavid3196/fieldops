using FieldOps.Application.Features.Dispatch;

namespace FieldOps.Api.Contracts;

/// <summary>
/// Body of POST /dispatch/visits/{id}/evaluation. Ids stay text so a malformed one is a field error. Unknown keys,
/// such as an organization id, are ignored (dispatch-calendar tenant isolation).
/// </summary>
public sealed record EvaluationRequestBody(
    string? Date,
    string? Start,
    string? End,
    List<string?>? TechnicianIds,
    string? PrimaryTechnicianId)
{
    public EvaluationBodyText ToText() =>
        new(Date, Start, End, TechnicianIds?.Select(id => id ?? string.Empty).ToList(), PrimaryTechnicianId);
}

/// <summary>Body of PUT /dispatch/visits/{id} (API contracts DispatchBody); booleans are nullable so a missing one is a 400.</summary>
public sealed record DispatchRequestBody(
    string? Date,
    string? Start,
    string? End,
    string? ArrivalWindow,
    List<string?>? TechnicianIds,
    string? PrimaryTechnicianId,
    string? DispatchNote,
    bool? NotifyCustomer,
    bool? SendTechnicianDetails,
    string? OverrideReason,
    string? UpdatedAt)
{
    public DispatchBodyText ToText() =>
        new(
            Date,
            Start,
            End,
            ArrivalWindow,
            TechnicianIds?.Select(id => id ?? string.Empty).ToList(),
            PrimaryTechnicianId,
            DispatchNote,
            NotifyCustomer,
            SendTechnicianDetails,
            OverrideReason,
            UpdatedAt);
}
