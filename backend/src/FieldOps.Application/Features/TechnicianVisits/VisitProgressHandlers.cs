using FieldOps.Domain.WorkOrders;

namespace FieldOps.Application.Features.TechnicianVisits;

public sealed record UpdateTaskInput(bool? IsCompleted, string? Notes);

public sealed record AddMaterialInput(decimal? Quantity, Guid? CatalogItemId, string? Description, string? Unit);

/// <summary>
/// The mobile-job-progress use cases (BR-01 to BR-14). Order of every mutation: BR-01 profile, a read-only access check
/// (visible visit, BR-02 primary, BR-06 status), the input validation, then the locked transaction of the store,
/// which repeats the whole chain authoritatively (BR-07). Photo bytes are read by the controller between the access
/// check and <see cref="AddEvidenceAsync"/>.
/// </summary>
public sealed class TechnicianVisitProgressHandler(ITechnicianVisitStore store, TimeProvider timeProvider)
{
    public Task<TechnicianVisitResult<VisitActionResult>> StartJobAsync(TravelCall call, Guid visitId, CancellationToken cancellationToken) =>
        TransitionAsync(call, visitId, ProgressTransition.StartJob, cancellationToken);

    public Task<TechnicianVisitResult<VisitActionResult>> PauseAsync(TravelCall call, Guid visitId, CancellationToken cancellationToken) =>
        TransitionAsync(call, visitId, ProgressTransition.Pause, cancellationToken);

    public Task<TechnicianVisitResult<VisitActionResult>> ResumeAsync(TravelCall call, Guid visitId, CancellationToken cancellationToken) =>
        TransitionAsync(call, visitId, ProgressTransition.Resume, cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> UpdateTaskAsync(
        TravelCall call, Guid visitId, Guid taskId, UpdateTaskInput input, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors =>
            {
                if (input.IsCompleted is null && input.Notes is null)
                {
                    errors["isCompleted"] = ["Send isCompleted or notes."];

                    return null;
                }

                var notes = input.Notes is null
                    ? null
                    : VisitProgressRules.OptionalText(input.Notes, VisitProgressRules.TaskNotesMaxLength, "notes", "The comment", errors) ?? string.Empty;

                return new UpdateTaskEdit(taskId, input.IsCompleted, notes);
            },
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> AddTaskAsync(
        TravelCall call, Guid visitId, string? label, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors => VisitProgressRules.RequiredText(label, VisitProgressRules.TaskLabelMaxLength, "label", "The task", errors) is { } text
                ? new AddTaskEdit(text)
                : null,
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> SetPlannedMaterialAsync(
        TravelCall call, Guid visitId, Guid plannedMaterialId, decimal? usedQuantity, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors => VisitProgressRules.Quantity(usedQuantity, allowZero: true, "usedQuantity", errors) is { } quantity
                ? new SetPlannedMaterialEdit(plannedMaterialId, quantity)
                : null,
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> AddMaterialAsync(
        TravelCall call, Guid visitId, AddMaterialInput input, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors =>
            {
                var quantity = VisitProgressRules.Quantity(input.Quantity, allowZero: false, "quantity", errors);
                var hasFreeText = input.Description is not null || input.Unit is not null;

                if (input.CatalogItemId is not null && hasFreeText)
                {
                    errors["catalogItemId"] = ["Choose a catalog item or enter a description and unit, not both."];

                    return null;
                }

                if (input.CatalogItemId is { } catalogItemId)
                {
                    if (catalogItemId == Guid.Empty)
                    {
                        errors["catalogItemId"] = ["Choose a catalog item."];
                    }

                    return quantity is { } catalogQuantity && errors.Count == 0
                        ? new AddMaterialEdit(catalogQuantity, catalogItemId, null, null)
                        : null;
                }

                if (!hasFreeText)
                {
                    errors["catalogItemId"] = ["Choose a catalog item or enter a description and unit."];

                    return null;
                }

                var description = VisitProgressRules.RequiredText(
                    input.Description, VisitProgressRules.DescriptionMaxLength, "description", "The description", errors);
                var unit = VisitProgressRules.RequiredText(input.Unit, VisitProgressRules.UnitMaxLength, "unit", "The unit", errors);

                return quantity is { } textQuantity && description is not null && unit is not null
                    ? new AddMaterialEdit(textQuantity, null, description, unit)
                    : null;
            },
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> SetMaterialAsync(
        TravelCall call, Guid visitId, Guid materialId, decimal? quantity, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors => VisitProgressRules.Quantity(quantity, allowZero: true, "quantity", errors) is { } value
                ? new SetMaterialEdit(materialId, value)
                : null,
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> SetNotesAsync(
        TravelCall call, Guid visitId, string? notes, CancellationToken cancellationToken) =>
        EditAsync(
            call,
            visitId,
            errors =>
            {
                var text = VisitProgressRules.OptionalText(
                    notes, VisitProgressRules.TechnicianNotesMaxLength, "notes", "The notes", errors);

                return errors.Count == 0 ? new SetNotesEdit(text) : null;
            },
            cancellationToken);

    public Task<TechnicianVisitResult<TechnicianVisitDetail>> DeleteEvidenceAsync(
        TravelCall call, Guid visitId, Guid evidenceId, CancellationToken cancellationToken) =>
        EditAsync(call, visitId, _ => new DeleteEvidenceEdit(evidenceId), cancellationToken);

    /// <summary>The read-only check run before the upload body is read: 404, 403 or 409 end the request without reading it.</summary>
    public async Task<TechnicianVisitResult<bool>> PrecheckEvidenceAsync(
        TravelCall call, Guid visitId, CancellationToken cancellationToken)
    {
        var (_, failure) = await CheckEditAsync<bool>(call, visitId, cancellationToken);

        return failure ?? TechnicianVisitResult<bool>.Ok(true);
    }

    public async Task<TechnicianVisitResult<TechnicianVisitDetail>> AddEvidenceAsync(
        TravelCall call,
        Guid visitId,
        string? type,
        string? fileName,
        string? declaredContentType,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var evidenceType = type?.Trim().ToLowerInvariant() switch
        {
            "before" => VisitEvidenceType.Before,
            "after" => VisitEvidenceType.After,
            _ => (VisitEvidenceType?)null,
        };

        if (evidenceType is null)
        {
            errors["type"] = ["Choose before or after."];
        }

        var check = VisitEvidenceContentValidator.Check(content, declaredContentType);

        if (!check.IsValid)
        {
            errors["file"] = [check.Error!];
        }

        return await EditAsync(
            call,
            visitId,
            _ => errors.Count > 0
                ? null
                : new AddEvidenceEdit(
                    evidenceType!.Value,
                    VisitEvidenceContentValidator.SanitizeFileName(fileName, check.ContentType!),
                    check.ContentType!,
                    content),
            cancellationToken,
            errors);
    }

    /// <summary>The read-only check run before the complete body is read: 404 or 403 end the request without reading it (mobile-job-completion BR-01, BR-02, BR-07).</summary>
    public async Task<TechnicianVisitResult<bool>> PrecheckCompleteAsync(
        TravelCall call, Guid visitId, CancellationToken cancellationToken)
    {
        var (_, failure) = await CheckCompleteAsync<bool>(call, visitId, cancellationToken);

        return failure ?? TechnicianVisitResult<bool>.Ok(true);
    }

    /// <summary>
    /// Complete job: profile, visibility and primary control, the acknowledgment validation, then the locked
    /// transaction of the store, which decides the status (BR-03) and the requirements (BR-04) authoritatively.
    /// </summary>
    public async Task<TechnicianVisitResult<VisitActionResult>> CompleteAsync(
        TravelCall call, Guid visitId, CompleteInput input, CancellationToken cancellationToken)
    {
        var (profile, failure) = await CheckCompleteAsync<VisitActionResult>(call, visitId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var completion = VisitCompletionRules.Validate(input, errors);

        if (completion is null)
        {
            return TechnicianVisitResult<VisitActionResult>.Invalid(errors);
        }

        var outcome = await store.CompleteAsync(
            Actor(call, profile), visitId, completion, timeProvider.GetUtcNow(), cancellationToken);

        return outcome.Kind switch
        {
            ProgressOutcomeKind.Saved => TechnicianVisitResult<VisitActionResult>.Ok(
                new VisitActionResult(outcome.Changed, TechnicianVisitRules.Detail(outcome.Found!, profile))),
            ProgressOutcomeKind.NotPrimary => TechnicianVisitResult<VisitActionResult>.Forbidden(
                TechnicianVisitCodes.NotPrimaryTechnician, VisitProgressMessages.NotPrimary),
            ProgressOutcomeKind.StatusInvalid => TechnicianVisitResult<VisitActionResult>.Conflict(
                TechnicianVisitCodes.VisitStatusInvalid, VisitProgressMessages.CompleteInvalid),
            ProgressOutcomeKind.RequirementsUnmet => TechnicianVisitResult<VisitActionResult>.Conflict(
                TechnicianVisitCodes.CompletionRequirementsUnmet, VisitProgressMessages.CompletionRequirementsUnmet),
            _ => TechnicianVisitResult<VisitActionResult>.NotFound(),
        };
    }

    public async Task<TechnicianVisitResult<IReadOnlyList<MaterialCatalogItem>>> SearchMaterialCatalogAsync(
        Guid organizationId, Guid membershipId, Guid visitId, string? search, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<IReadOnlyList<MaterialCatalogItem>>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        if (await store.GetProgressAccessAsync(organizationId, profile.Id, visitId, cancellationToken) is null)
        {
            return TechnicianVisitResult<IReadOnlyList<MaterialCatalogItem>>.NotFound();
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var text = VisitProgressRules.Search(search, errors);

        if (text is null)
        {
            return TechnicianVisitResult<IReadOnlyList<MaterialCatalogItem>>.Invalid(errors);
        }

        return TechnicianVisitResult<IReadOnlyList<MaterialCatalogItem>>.Ok(
            await store.SearchMaterialCatalogAsync(organizationId, text, cancellationToken));
    }

    public async Task<TechnicianVisitResult<VisitEvidenceImage>> GetEvidenceAsync(
        Guid organizationId, Guid membershipId, Guid visitId, Guid evidenceId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<VisitEvidenceImage>(
            store, organizationId, membershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var image = await store.FindEvidenceAsync(organizationId, profile.Id, visitId, evidenceId, cancellationToken);

        return image is null
            ? TechnicianVisitResult<VisitEvidenceImage>.NotFound()
            : TechnicianVisitResult<VisitEvidenceImage>.Ok(image);
    }

    private async Task<TechnicianVisitResult<VisitActionResult>> TransitionAsync(
        TravelCall call, Guid visitId, ProgressTransition transition, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<VisitActionResult>(
            store, call.OrganizationId, call.MembershipId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        var outcome = await store.TransitionAsync(
            Actor(call, profile), visitId, transition, timeProvider.GetUtcNow(), cancellationToken);

        return outcome.Kind switch
        {
            ProgressOutcomeKind.Saved => TechnicianVisitResult<VisitActionResult>.Ok(
                new VisitActionResult(outcome.Changed, TechnicianVisitRules.Detail(outcome.Found!, profile))),
            ProgressOutcomeKind.NotPrimary => TechnicianVisitResult<VisitActionResult>.Forbidden(
                TechnicianVisitCodes.NotPrimaryTechnician, VisitProgressMessages.NotPrimary),
            ProgressOutcomeKind.StatusInvalid => TechnicianVisitResult<VisitActionResult>.Conflict(
                TechnicianVisitCodes.VisitStatusInvalid,
                transition == ProgressTransition.StartJob
                    ? VisitProgressMessages.StartInvalid
                    : VisitProgressMessages.PauseResumeInvalid),
            _ => TechnicianVisitResult<VisitActionResult>.NotFound(),
        };
    }

    private async Task<TechnicianVisitResult<TechnicianVisitDetail>> EditAsync(
        TravelCall call,
        Guid visitId,
        Func<Dictionary<string, string[]>, VisitEdit?> build,
        CancellationToken cancellationToken,
        Dictionary<string, string[]>? errors = null)
    {
        var (profile, failure) = await CheckEditAsync<TechnicianVisitDetail>(call, visitId, cancellationToken);

        if (profile is null)
        {
            return failure!;
        }

        errors ??= new Dictionary<string, string[]>(StringComparer.Ordinal);

        var edit = build(errors);

        if (edit is null || errors.Count > 0)
        {
            return TechnicianVisitResult<TechnicianVisitDetail>.Invalid(errors);
        }

        var outcome = await store.EditAsync(Actor(call, profile), visitId, edit, timeProvider.GetUtcNow(), cancellationToken);

        return outcome.Kind switch
        {
            ProgressOutcomeKind.Saved => TechnicianVisitResult<TechnicianVisitDetail>.Ok(
                TechnicianVisitRules.Detail(outcome.Found!, profile)),
            ProgressOutcomeKind.NotPrimary => TechnicianVisitResult<TechnicianVisitDetail>.Forbidden(
                TechnicianVisitCodes.NotPrimaryTechnician, VisitProgressMessages.NotPrimary),
            ProgressOutcomeKind.StatusInvalid => TechnicianVisitResult<TechnicianVisitDetail>.Conflict(
                TechnicianVisitCodes.VisitStatusInvalid, VisitProgressMessages.EditInvalid),
            ProgressOutcomeKind.LimitReached => LimitReached(outcome.LimitCode!),
            _ => TechnicianVisitResult<TechnicianVisitDetail>.NotFound(),
        };
    }

    // BR-01, BR-02, BR-06 over the lock-free read: the first refusal wins, in that order.
    private async Task<(TechnicianVisitProfile? Profile, TechnicianVisitResult<T>? Failure)> CheckEditAsync<T>(
        TravelCall call, Guid visitId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<T>(
            store, call.OrganizationId, call.MembershipId, cancellationToken);

        if (profile is null)
        {
            return (null, failure);
        }

        var access = await store.GetProgressAccessAsync(call.OrganizationId, profile.Id, visitId, cancellationToken);

        if (access is null)
        {
            return (null, TechnicianVisitResult<T>.NotFound());
        }

        if (!access.IsPrimary)
        {
            return (null, TechnicianVisitResult<T>.Forbidden(
                TechnicianVisitCodes.NotPrimaryTechnician, VisitProgressMessages.NotPrimary));
        }

        return access.Status is VisitStatus.InProgress or VisitStatus.Paused
            ? (profile, null)
            : (null, TechnicianVisitResult<T>.Conflict(TechnicianVisitCodes.VisitStatusInvalid, VisitProgressMessages.EditInvalid));
    }

    // BR-01, BR-02 over the lock-free read; the status is decided later, after the body validation and the locks (BR-03).
    private async Task<(TechnicianVisitProfile? Profile, TechnicianVisitResult<T>? Failure)> CheckCompleteAsync<T>(
        TravelCall call, Guid visitId, CancellationToken cancellationToken)
    {
        var (profile, failure) = await TechnicianVisitRules.ResolveAsync<T>(
            store, call.OrganizationId, call.MembershipId, cancellationToken);

        if (profile is null)
        {
            return (null, failure);
        }

        var access = await store.GetProgressAccessAsync(call.OrganizationId, profile.Id, visitId, cancellationToken);

        if (access is null)
        {
            return (null, TechnicianVisitResult<T>.NotFound());
        }

        return access.IsPrimary
            ? (profile, null)
            : (null, TechnicianVisitResult<T>.Forbidden(TechnicianVisitCodes.NotPrimaryTechnician, VisitProgressMessages.NotPrimary));
    }

    private static TechnicianVisitResult<TechnicianVisitDetail> LimitReached(string code) =>
        TechnicianVisitResult<TechnicianVisitDetail>.Conflict(
            code,
            code switch
            {
                TechnicianVisitCodes.TaskLimitReached => VisitProgressMessages.TaskLimit,
                TechnicianVisitCodes.MaterialLimitReached => VisitProgressMessages.MaterialLimit,
                _ => VisitProgressMessages.EvidenceLimit,
            });

    private static ProgressActor Actor(TravelCall call, TechnicianVisitProfile profile) =>
        new(call.OrganizationId, call.UserId, profile.Id, profile.ZoneId, call.IpAddress);
}
