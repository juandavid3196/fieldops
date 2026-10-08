using FieldOps.Application.Features.BillingReview;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class BillingReviewStore
{
    /// <summary>
    /// The review update (BR-15): the work order row is locked first, the queue membership is checked again on the locked
    /// row and a request identical to the current state writes nothing. Audit rows never carry the note text.
    /// </summary>
    public async Task<BillingOutcome<ReviewResult>> UpdateReviewAsync(
        BillingActor actor, Guid workOrderId, ReviewInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (!await LockWorkOrderAsync(actor, workOrderId, cancellationToken)
            || !await QueueOrders(organizationId, actor.Scope).AnyAsync(candidate => candidate.Id == workOrderId, cancellationToken))
        {
            return new BillingOutcome<ReviewResult>.NotFound();
        }

        var order = await dbContext.WorkOrders.SingleAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Id == workOrderId, cancellationToken);
        var note = input.NoteSent ? input.Note : order.BillingReviewNote;
        var noteBefore = order.BillingReviewNote;
        var wasMarked = order.BillingFollowUpAt is not null;

        if (order.UpdateBillingReview(note, input.FollowUp, actor.UserId, now))
        {
            AddReviewAudits(actor, order.Id, order.BranchId, noteBefore, order.BillingReviewNote, wasMarked, order.BillingFollowUpAt is not null, input.Reason);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new BillingOutcome<ReviewResult>.Succeeded(await ToReviewResultAsync(order.BillingReviewNote, order.BillingFollowUpAt, order.BillingFollowUpByUserId, cancellationToken));
    }

    /// <summary>Locks the work order row inside the organization and the caller scope; false when it does not exist for the caller.</summary>
    private async Task<bool> LockWorkOrderAsync(BillingActor actor, Guid workOrderId, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var scopeAll = actor.Scope.All;
        var scopeIds = actor.Scope.BranchIds.ToArray();

        var locked = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM work_orders
                WHERE id = {workOrderId} AND organization_id = {organizationId}
                  AND ({scopeAll} OR branch_id = ANY({scopeIds}))
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return locked.Count == 1;
    }

    /// <summary>BR-15 audit rows: ids, lengths and reasons only, never the note text.</summary>
    private void AddReviewAudits(
        BillingActor actor,
        Guid orderId,
        Guid branchId,
        string? noteBefore,
        string? noteAfter,
        bool wasMarked,
        bool isMarked,
        string reason)
    {
        void Add(string action, object metadata) =>
            dbContext.AuditLogs.Add(AuditLog.Create(
                actor.OrganizationId, action, AuditWorkOrder, actor.UserId, orderId, branchId, actor.IpAddress, null, null, Serialize(metadata)));

        if (!string.Equals(noteBefore, noteAfter, StringComparison.Ordinal))
        {
            Add("work_order.billing_note_updated", new Dictionary<string, object?> { ["length"] = noteAfter?.Length ?? 0 });
        }

        if (!wasMarked && isMarked)
        {
            Add("work_order.billing_follow_up_marked", new Dictionary<string, object?> { ["reason"] = reason });
        }
        else if (wasMarked && !isMarked)
        {
            Add("work_order.billing_follow_up_cleared", new Dictionary<string, object?> { ["reason"] = reason });
        }
    }

    private async Task<ReviewResult> ToReviewResultAsync(
        string? note, DateTimeOffset? followUpAt, Guid? followUpBy, CancellationToken cancellationToken)
    {
        FollowUpView? followUp = null;

        if (followUpAt is { } at && followUpBy is { } by)
        {
            followUp = new FollowUpView(at, await ReadUserNameAsync(by, cancellationToken));
        }

        return new ReviewResult(note, followUp);
    }
}
