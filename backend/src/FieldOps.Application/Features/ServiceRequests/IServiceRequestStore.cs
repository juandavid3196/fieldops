using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.ServiceRequests;

/// <summary>Persistence port of the requests pipeline (FR-02 to FR-17). Every method is scoped to one organization.</summary>
public interface IServiceRequestStore
{
    Task<RequestOrganizationContext?> GetOrganizationContextAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Checks the assignee and category filter ids against the organization (BR-06); errors keyed by query parameter.</summary>
    Task<IReadOnlyDictionary<string, string[]>> ValidateFilterIdsAsync(
        Guid organizationId, PipelineFilter filter, CancellationToken cancellationToken);

    Task<PipelineView> GetPipelineAsync(
        Guid organizationId,
        BranchScope scope,
        PipelineFilter filter,
        PipelinePage page,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<RequestMetrics> GetMetricsAsync(
        Guid organizationId, BranchScope scope, DateTimeOffset now, CancellationToken cancellationToken);

    Task<RequestOptions> GetOptionsAsync(Guid organizationId, BranchScope scope, CancellationToken cancellationToken);

    Task<CustomerOptions> GetCustomerOptionsAsync(
        Guid organizationId, BranchScope scope, string? search, CancellationToken cancellationToken);

    /// <summary>The detail of a visible request in any status, otherwise null (BR-02).</summary>
    Task<RequestDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid requestId, CancellationToken cancellationToken);

    /// <summary>The attachment content of a visible request, otherwise null (BR-16).</summary>
    Task<AttachmentDownload?> GetAttachmentAsync(
        Guid organizationId, BranchScope scope, Guid requestId, Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one mutation in a transaction that first locks the request row, so conflicting mutations
    /// serialize and the loser sees the new status (FR-06).
    /// </summary>
    Task<RequestMutationOutcome> MutateAsync(
        RequestActor actor, Guid requestId, RequestMutation mutation, CancellationToken cancellationToken);

    Task<RequestCreationOutcome> CreateInternalAsync(
        RequestActor actor, InternalRequestInput input, CancellationToken cancellationToken);
}

/// <summary>Sends the information-request email after the commit (BR-13); implementations never throw for a delivery failure.</summary>
public interface IRequestInformationNotifier
{
    Task SendAsync(InformationRequestEmail email, CancellationToken cancellationToken);
}
