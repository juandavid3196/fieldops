using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class ServiceRequestStore
{
    private const string SerializationFailure = "40001";

    private const string DeadlockDetected = "40P01";

    private const string ExclusionViolation = "23P01";

    public async Task<RequestMutationOutcome> MutateAsync(
        RequestActor actor, Guid requestId, RequestMutation mutation, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync(actor, requestId, mutation, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();

            return new RequestMutationOutcome.Conflict();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException postgres)
        {
            dbContext.ChangeTracker.Clear();

            if (IsConflictState(postgres.SqlState))
            {
                return new RequestMutationOutcome.Conflict();
            }

            // The database detail can quote the failing row (personal data): only state and constraint travel.
            throw new InvalidOperationException(
                $"The request change could not be saved (SqlState {postgres.SqlState}, constraint {postgres.ConstraintName}).");
        }
        catch (PostgresException postgres) when (IsConflictState(postgres.SqlState))
        {
            dbContext.ChangeTracker.Clear();

            return new RequestMutationOutcome.Conflict();
        }
    }

    private static bool IsConflictState(string sqlState) =>
        sqlState is SerializationFailure or DeadlockDetected or ExclusionViolation;

    private async Task<RequestMutationOutcome> ExecuteAsync(
        RequestActor actor, Guid requestId, RequestMutation mutation, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Raw SQL: EF has no declarative row-lock API. Tenant and branch scope are part of this first
        // statement, so a missing, foreign or out-of-scope request is one identical "not found". Concurrent
        // mutations of the request queue here and re-read the committed status when they get the lock.
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();
        var locked = await dbContext.Database
            .SqlQuery<string>(
                $"""
                SELECT status::text AS "Value" FROM service_requests
                WHERE id = {requestId} AND organization_id = {actor.OrganizationId}
                  AND ({scopeAll} OR branch_id IS NULL OR branch_id = ANY({scopeIds}))
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count != 1)
        {
            return new RequestMutationOutcome.NotFound();
        }

        var request = await dbContext.ServiceRequests.SingleAsync(
            candidate => candidate.Id == requestId && candidate.OrganizationId == actor.OrganizationId,
            cancellationToken);

        var step = await ApplyAsync(actor, request, mutation, cancellationToken);

        if (step.Failure is not null)
        {
            await transaction.RollbackAsync(cancellationToken);

            return step.Failure;
        }

        if (step.Changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var detail = await GetDetailAsync(actor.OrganizationId, actor.Scope, requestId, cancellationToken)
            ?? throw new InvalidOperationException("The request is no longer available.");

        return new RequestMutationOutcome.Succeeded(detail, step.Email);
    }

    private Task<Step> ApplyAsync(
        RequestActor actor, ServiceRequest request, RequestMutation mutation, CancellationToken cancellationToken) =>
        mutation switch
        {
            RequestMutation.StartReview => TransitionAsync(actor, request, RequestAction.StartReview, "service_request.status_changed"),
            RequestMutation.MarkReadyForQuote => TransitionAsync(actor, request, RequestAction.MarkReadyForQuote, "service_request.ready_for_quote"),
            RequestMutation.MoveToReview => TransitionAsync(actor, request, RequestAction.MoveToReview, "service_request.status_changed"),
            RequestMutation.Assign assign => AssignAsync(actor, request, assign, cancellationToken),
            RequestMutation.ChangePriority priority => PriorityAsync(actor, request, priority),
            RequestMutation.SetBranch branch => SetBranchAsync(actor, request, branch, cancellationToken),
            RequestMutation.AddNote note => AddNoteAsync(actor, request, note),
            RequestMutation.RequestInformation information => RequestInformationAsync(actor, request, information, cancellationToken),
            RequestMutation.LogResponse response => LogResponseAsync(actor, request, response),
            RequestMutation.ScheduleAssessment schedule => ScheduleAsync(actor, request, schedule, cancellationToken),
            RequestMutation.RescheduleAssessment reschedule => RescheduleAsync(actor, request, reschedule, cancellationToken),
            RequestMutation.CancelAssessment => CancelAssessmentAsync(actor, request, cancellationToken),
            RequestMutation.CompleteAssessment => CompleteAssessmentAsync(actor, request, cancellationToken),
            RequestMutation.CancelRequest cancel => CancelRequestAsync(actor, request, cancel, cancellationToken),
            RequestMutation.AddAttachments attachments => AddAttachmentsAsync(actor, request, attachments, cancellationToken),
            _ => throw new InvalidOperationException("Unknown request mutation."),
        };

    private Task<Step> TransitionAsync(RequestActor actor, ServiceRequest request, RequestAction action, string auditAction)
    {
        if (!RequestTransitions.TryApply(request.Status, action, out var to))
        {
            return Task.FromResult(Step.Fail(new RequestMutationOutcome.Conflict()));
        }

        var from = request.Status;
        Move(actor, request, to);
        Audit(actor, request, auditAction, StatusSide(from), StatusSide(to), null);

        return Task.FromResult(Step.Done());
    }

    private async Task<Step> AssignAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.Assign mutation, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.Assign, out var to))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        if (request.AssignedDispatcherUserId == mutation.AssigneeUserId)
        {
            return Step.NoOp();
        }

        if (mutation.AssigneeUserId is { } assigneeId
            && !await IsEligibleAssigneeAsync(actor.OrganizationId, assigneeId, request.BranchId, cancellationToken))
        {
            return Step.Fail(Invalid("assigneeUserId", ServiceRequestMessages.AssigneeNotAllowed));
        }

        var from = request.Status;
        var before = new Dictionary<string, object?> { ["assigneeUserId"] = request.AssignedDispatcherUserId };
        var after = new Dictionary<string, object?> { ["assigneeUserId"] = mutation.AssigneeUserId };

        request.AssignTo(mutation.AssigneeUserId);

        if (to != from)
        {
            Move(actor, request, to);
            before["status"] = RequestTransitions.Code(from);
            after["status"] = RequestTransitions.Code(to);
        }

        Audit(actor, request, "service_request.assigned", before, after, null);

        return Step.Done();
    }

    private Task<Step> PriorityAsync(RequestActor actor, ServiceRequest request, RequestMutation.ChangePriority mutation)
    {
        if (!RequestTransitions.IsOpen(request.Status))
        {
            return Task.FromResult(Step.Fail(new RequestMutationOutcome.Conflict()));
        }

        if (request.Urgency == mutation.Urgency)
        {
            return Task.FromResult(Step.NoOp());
        }

        var before = new Dictionary<string, object?> { ["urgency"] = request.Urgency };
        request.ChangeUrgency(mutation.Urgency);
        Audit(
            actor,
            request,
            "service_request.priority_changed",
            before,
            new Dictionary<string, object?> { ["urgency"] = request.Urgency },
            null);

        return Task.FromResult(Step.Done());
    }

    private async Task<Step> SetBranchAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.SetBranch mutation, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.IsOpen(request.Status))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        if (request.BranchId == mutation.BranchId)
        {
            return Step.NoOp();
        }

        var error = await BranchErrorAsync(actor, request, mutation.BranchId, cancellationToken);

        if (error is not null)
        {
            return Step.Fail(Invalid("branchId", error));
        }

        var before = new Dictionary<string, object?> { ["branchId"] = request.BranchId };
        request.SetBranch(mutation.BranchId);
        Audit(
            actor,
            request,
            "service_request.branch_changed",
            before,
            new Dictionary<string, object?> { ["branchId"] = request.BranchId },
            null);

        return Step.Done();
    }

    private Task<Step> AddNoteAsync(RequestActor actor, ServiceRequest request, RequestMutation.AddNote mutation)
    {
        if (!RequestTransitions.IsOpen(request.Status))
        {
            return Task.FromResult(Step.Fail(new RequestMutationOutcome.Conflict()));
        }

        var message = RequestMessage.Create(
            actor.OrganizationId, request.Id, MessageVisibility.Internal, mutation.Body, authorUserId: actor.UserId);
        dbContext.RequestMessages.Add(message);
        request.Touch();
        Audit(actor, request, "service_request.internal_note_added", null, null, new { messageId = message.Id });

        return Task.FromResult(Step.Done());
    }

    private async Task<Step> RequestInformationAsync(
        RequestActor actor,
        ServiceRequest request,
        RequestMutation.RequestInformation mutation,
        CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.RequestInformation, out var to))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        var contact = request.ContactId is { } contactId
            ? await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == contactId && candidate.OrganizationId == actor.OrganizationId)
                .Select(candidate => new { candidate.FirstName, candidate.Email, candidate.IsActive })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var recipient = contact is { IsActive: true } && !string.IsNullOrWhiteSpace(contact.Email)
            ? contact.Email.Trim()
            : string.IsNullOrWhiteSpace(request.GuestEmail) ? null : request.GuestEmail.Trim();

        if (recipient is null)
        {
            return Step.Fail(Invalid("body", ServiceRequestMessages.NoEmail));
        }

        var organization = await GetOrganizationContextAsync(actor.OrganizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");

        var from = request.Status;
        var message = RequestMessage.Create(
            actor.OrganizationId, request.Id, MessageVisibility.Customer, mutation.Body, authorUserId: actor.UserId);
        dbContext.RequestMessages.Add(message);
        request.Touch();

        object? before = null;
        object? after = null;

        if (to != from)
        {
            Move(actor, request, to);
            before = StatusSide(from);
            after = StatusSide(to);
        }

        Audit(actor, request, "service_request.information_requested", before, after, new { messageId = message.Id });

        return Step.Done(new InformationRequestEmail(
            request.Id,
            RequestCardRules.DisplayNumber(organization.RequestPrefix, request.RequestNumber),
            recipient,
            contact?.FirstName,
            organization.Name,
            organization.Phone,
            message.Body));
    }

    private Task<Step> LogResponseAsync(RequestActor actor, ServiceRequest request, RequestMutation.LogResponse mutation)
    {
        if (!RequestTransitions.IsOpen(request.Status))
        {
            return Task.FromResult(Step.Fail(new RequestMutationOutcome.Conflict()));
        }

        if (request.ContactId is not { } contactId)
        {
            return Task.FromResult(Step.Fail(Invalid("body", ServiceRequestMessages.NoContact)));
        }

        var message = RequestMessage.Create(
            actor.OrganizationId,
            request.Id,
            MessageVisibility.Customer,
            mutation.Body,
            authorUserId: actor.UserId,
            authorContactId: contactId);
        dbContext.RequestMessages.Add(message);
        request.Touch();
        Audit(actor, request, "service_request.customer_response_logged", null, null, new { messageId = message.Id });

        return Task.FromResult(Step.Done());
    }

    private async Task<Step> ScheduleAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.ScheduleAssessment mutation, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.ScheduleAssessment, out var to)
            || await ActiveAssessmentExistsAsync(actor.OrganizationId, request.Id, cancellationToken))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var branchId = request.BranchId;
        var branchIsNew = false;

        if (branchId is null)
        {
            if (mutation.BranchId is not { } requested)
            {
                errors["branchId"] = [ServiceRequestMessages.BranchNotAllowed];
            }
            else
            {
                var error = await BranchErrorAsync(actor, request, requested, cancellationToken);

                if (error is null)
                {
                    branchId = requested;
                    branchIsNew = true;
                }
                else
                {
                    errors["branchId"] = [error];
                }
            }
        }

        if (branchId is { } slotBranch && mutation.TechnicianId is { } technicianId)
        {
            await ValidateTechnicianAsync(
                actor.OrganizationId, slotBranch, technicianId, mutation.Start, mutation.End, Guid.Empty, errors, cancellationToken);
        }

        if (errors.Count > 0)
        {
            return Step.Fail(new RequestMutationOutcome.Invalid(errors));
        }

        var from = request.Status;

        if (branchIsNew)
        {
            request.SetBranch(branchId!.Value);
        }

        var assessment = Assessment.Create(
            actor.OrganizationId, request.Id, mutation.Start, mutation.End, actor.UserId, mutation.TechnicianId);
        dbContext.Assessments.Add(assessment);
        Move(actor, request, to);
        Audit(
            actor,
            request,
            "service_request.assessment_scheduled",
            StatusSide(from),
            StatusSide(to),
            AssessmentMetadata(assessment));

        return Step.Done();
    }

    private async Task<Step> RescheduleAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.RescheduleAssessment mutation, CancellationToken cancellationToken)
    {
        var assessment = request.Status == RequestStatus.AssessmentScheduled
            ? await LoadActiveAssessmentAsync(actor.OrganizationId, request.Id, cancellationToken)
            : null;

        if (assessment is null || request.BranchId is not { } branchId)
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (mutation.TechnicianId is { } technicianId)
        {
            await ValidateTechnicianAsync(
                actor.OrganizationId, branchId, technicianId, mutation.Start, mutation.End, assessment.Id, errors, cancellationToken);
        }

        if (errors.Count > 0)
        {
            return Step.Fail(new RequestMutationOutcome.Invalid(errors));
        }

        assessment.Reschedule(mutation.Start, mutation.End, mutation.TechnicianId);
        request.Touch();
        Audit(actor, request, "service_request.assessment_rescheduled", null, null, AssessmentMetadata(assessment));

        return Step.Done();
    }

    private async Task<Step> CancelAssessmentAsync(
        RequestActor actor, ServiceRequest request, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.CancelAssessment, out var to)
            || await LoadActiveAssessmentAsync(actor.OrganizationId, request.Id, cancellationToken) is not { } assessment)
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        var from = request.Status;
        assessment.Cancel();
        Move(actor, request, to);
        Audit(
            actor,
            request,
            "service_request.assessment_cancelled",
            StatusSide(from),
            StatusSide(to),
            AssessmentMetadata(assessment));

        return Step.Done();
    }

    private async Task<Step> CompleteAssessmentAsync(
        RequestActor actor, ServiceRequest request, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.CompleteAssessment, out var to)
            || await LoadActiveAssessmentAsync(actor.OrganizationId, request.Id, cancellationToken) is not { } assessment)
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        if (assessment.ScheduledStart > timeProvider.GetUtcNow())
        {
            return Step.Fail(new RequestMutationOutcome.Conflict(ServiceRequestMessages.AssessmentNotStartedTitle));
        }

        var from = request.Status;
        assessment.Complete();
        Move(actor, request, to);
        Audit(
            actor,
            request,
            "service_request.assessment_completed",
            StatusSide(from),
            StatusSide(to),
            new { assessmentId = assessment.Id });

        return Step.Done();
    }

    private async Task<Step> CancelRequestAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.CancelRequest mutation, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.TryApply(request.Status, RequestAction.Cancel, out var to))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        if (await LoadActiveAssessmentAsync(actor.OrganizationId, request.Id, cancellationToken) is { } assessment)
        {
            assessment.Cancel();
        }

        var from = request.Status;
        Move(actor, request, to, mutation.Reason);
        Audit(actor, request, "service_request.status_changed", StatusSide(from), StatusSide(to), null);

        return Step.Done();
    }

    private async Task<Step> AddAttachmentsAsync(
        RequestActor actor, ServiceRequest request, RequestMutation.AddAttachments mutation, CancellationToken cancellationToken)
    {
        if (!RequestTransitions.IsOpen(request.Status))
        {
            return Step.Fail(new RequestMutationOutcome.Conflict());
        }

        var existing = await dbContext.RequestAttachments.AsNoTracking().CountAsync(
            attachment => attachment.OrganizationId == actor.OrganizationId && attachment.RequestId == request.Id,
            cancellationToken);

        if (existing + mutation.Files.Count > ServiceRequestMessages.MaxAttachments)
        {
            return Step.Fail(Invalid("attachments", ServiceRequestMessages.TooManyAttachments));
        }

        foreach (var file in mutation.Files)
        {
            dbContext.RequestAttachments.Add(RequestAttachment.Create(
                actor.OrganizationId, request.Id, file.FileName, file.MimeType, file.Content, actor.UserId));
        }

        request.Touch();
        Audit(actor, request, "service_request.attachments_added", null, null, new { count = mutation.Files.Count });

        return Step.Done();
    }

    /// <summary>Moves the request and records exactly one status history row (States and transitions).</summary>
    private void Move(RequestActor actor, ServiceRequest request, RequestStatus to, string? reason = null)
    {
        var from = request.Status;
        request.ChangeStatus(to);
        dbContext.RequestStatusHistories.Add(
            RequestStatusHistory.Create(actor.OrganizationId, request.Id, from, to, actor.UserId, reason));
    }

    // BR-20: ids and status codes only; never names, contact data, bodies or file names.
    private void Audit(
        RequestActor actor, ServiceRequest request, string action, object? before, object? after, object? metadata) =>
        dbContext.AuditLogs.Add(AuditLog.Create(
            actor.OrganizationId,
            action,
            AuditEntityType,
            actor.UserId,
            request.Id,
            request.BranchId,
            actor.IpAddress,
            Serialize(before),
            Serialize(after),
            Serialize(metadata)));

    private static Dictionary<string, object?> StatusSide(RequestStatus status) =>
        new() { ["status"] = RequestTransitions.Code(status) };

    private static object AssessmentMetadata(Assessment assessment) =>
        new
        {
            assessmentId = assessment.Id,
            technicianId = assessment.TechnicianId,
            start = assessment.ScheduledStart,
            end = assessment.ScheduledEnd,
        };

    private static RequestMutationOutcome.Invalid Invalid(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    private Task<bool> ActiveAssessmentExistsAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Assessments.AsNoTracking().AnyAsync(
            assessment => assessment.OrganizationId == organizationId
                && assessment.RequestId == requestId
                && assessment.Status == AssessmentStatus.Scheduled,
            cancellationToken);

    private Task<Assessment?> LoadActiveAssessmentAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Assessments
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.RequestId == requestId
                && assessment.Status == AssessmentStatus.Scheduled)
            .OrderByDescending(assessment => assessment.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>BR-11: an active in-scope branch the current assignee can also work in. Null when allowed.</summary>
    private async Task<string?> BranchErrorAsync(
        RequestActor actor, ServiceRequest request, Guid branchId, CancellationToken cancellationToken)
    {
        var query = dbContext.Branches.AsNoTracking()
            .Where(branch => branch.Id == branchId && branch.OrganizationId == actor.OrganizationId && branch.IsActive);

        if (!actor.Scope.All)
        {
            var ids = actor.Scope.BranchIds.ToArray();
            query = query.Where(branch => ids.Contains(branch.Id));
        }

        if (!await query.AnyAsync(cancellationToken))
        {
            return ServiceRequestMessages.BranchNotAllowed;
        }

        if (request.AssignedDispatcherUserId is { } assigneeId
            && !await MemberHasBranchAsync(actor.OrganizationId, assigneeId, branchId, cancellationToken))
        {
            return ServiceRequestMessages.AssigneeLacksBranch;
        }

        return null;
    }

    private async Task<bool> MemberHasBranchAsync(
        Guid organizationId, Guid userId, Guid branchId, CancellationToken cancellationToken)
    {
        var membershipId = await dbContext.OrganizationUsers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId && member.UserId == userId)
            .Select(member => (Guid?)member.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (membershipId is null)
        {
            return false;
        }

        var scope = await new BranchScopeResolver(dbContext).ResolveAsync(organizationId, membershipId.Value, cancellationToken);

        return scope.Contains(branchId);
    }

    /// <summary>BR-09: an active owner or dispatcher who has the request branch in scope when it has one.</summary>
    private async Task<bool> IsEligibleAssigneeAsync(
        Guid organizationId, Guid userId, Guid? branchId, CancellationToken cancellationToken) =>
        await IsManagerMemberAsync(organizationId, userId, cancellationToken)
        && (branchId is not { } branch || await MemberHasBranchAsync(organizationId, userId, branch, cancellationToken));

    /// <summary>
    /// BR-15: the technician is an active profile of the request branch and has no other scheduled
    /// assessment overlapping the slot. The profile row is locked first so concurrent schedules of the
    /// same technician serialize (lock order: request, technician).
    /// </summary>
    private async Task ValidateTechnicianAsync(
        Guid organizationId,
        Guid branchId,
        Guid technicianId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid excludeAssessmentId,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM technician_profiles
                WHERE id = {technicianId} AND organization_id = {organizationId}
                  AND branch_id = {branchId} AND status = 'active'
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            errors["technicianId"] = [ServiceRequestMessages.TechnicianNotAllowed];

            return;
        }

        var busy = await dbContext.Assessments.AsNoTracking().AnyAsync(
            assessment => assessment.OrganizationId == organizationId
                && assessment.TechnicianId == technicianId
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart < end
                && assessment.ScheduledEnd > start
                && assessment.Id != excludeAssessmentId,
            cancellationToken);

        if (busy)
        {
            errors["technicianId"] = [ServiceRequestMessages.TechnicianBusy];
        }
    }

    private readonly record struct Step(RequestMutationOutcome? Failure, bool Changed, InformationRequestEmail? Email)
    {
        public static Step Done(InformationRequestEmail? email = null) => new(null, true, email);

        public static Step NoOp() => new(null, false, null);

        public static Step Fail(RequestMutationOutcome failure) => new(failure, false, null);
    }
}
