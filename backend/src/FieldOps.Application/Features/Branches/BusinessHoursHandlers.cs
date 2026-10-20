using System.Text.Json;
using FieldOps.Application.Validation;
using FieldOps.Domain.Notifications;

namespace FieldOps.Application.Features.Branches;

/// <summary>Reads one branch's business hours, scoped by company-settings-and-branches BR-11 access (FR-03).</summary>
public sealed class GetBusinessHoursHandler(IBranchStore store)
{
    public async Task<BusinessHoursView?> HandleAsync(
        Guid organizationId, Guid membershipId, Guid branchId, bool canManage, CancellationToken cancellationToken)
    {
        var detail = await store.GetDetailAsync(organizationId, membershipId, branchId, cancellationToken);

        return detail is null
            ? null
            : new BusinessHoursView(
                detail.Id,
                detail.Name,
                detail.Timezone,
                detail.UpdatedAt,
                canManage,
                BusinessHoursRules.FromStored(detail.BusinessHours));
    }
}

/// <summary>
/// Validates and stores the seven days (FR-04): BR-01 to BR-04 shape, BR-06 stale check, BR-07 no-op and the BR-08
/// audit row written in the same save as the branch.
/// </summary>
public sealed class UpdateBusinessHoursHandler(IBranchStore store, TimeProvider timeProvider)
{
    public const string AuditAction = "branch.business_hours_updated";

    public async Task<UpdateBusinessHoursResult> HandleAsync(
        UpdateBusinessHoursCommand command, CancellationToken cancellationToken)
    {
        var branch = await store.GetForManageAsync(command.OrganizationId, command.BranchId, cancellationToken);

        if (branch is null)
        {
            return new UpdateBusinessHoursResult.NotFound();
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var daysValid = BusinessHoursRules.TryValidate(
            command.Days,
            (key, message) =>
            {
                if (!errors.TryGetValue(key, out var messages))
                {
                    errors[key] = messages = [];
                }

                messages.Add(message);
            },
            out var days,
            out var canonicalJson);

        var updatedAtValid = UpdatedAtValidation.TryParse(command.UpdatedAt, out var requestedUpdatedAt);

        if (!updatedAtValid)
        {
            errors["updatedAt"] = [UpdatedAtValidation.InvalidMessage];
        }

        if (!daysValid || !updatedAtValid)
        {
            return new UpdateBusinessHoursResult.Invalid(
                errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        }

        // Fast path; the UpdatedAt concurrency token still catches a race before SaveChangesAsync commits.
        if (Truncate(requestedUpdatedAt) != Truncate(branch.UpdatedAt))
        {
            return new UpdateBusinessHoursResult.Stale();
        }

        var before = BusinessHoursRules.FromStored(branch.BusinessHours);
        var changed = BusinessHoursRules.ChangedDays(before, days);

        if (changed.Count == 0)
        {
            return new UpdateBusinessHoursResult.Succeeded(ToView(branch, days));
        }

        var now = Truncate(timeProvider.GetUtcNow());

        if (now <= branch.UpdatedAt)
        {
            now = branch.UpdatedAt.AddTicks(10);
        }

        branch.UpdateBusinessHours(canonicalJson, now);

        var auditLog = AuditLog.Create(
            command.OrganizationId,
            AuditAction,
            UpdateBranchHandler.BranchAuditEntityType,
            actorUserId: command.ActorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: command.ClientIp,
            beforeData: Serialize(changed.Select(pair => pair.Before)),
            afterData: Serialize(changed.Select(pair => pair.After)),
            metadata: JsonSerializer.Serialize(
                new Dictionary<string, object?> { ["changedDays"] = changed.Select(pair => pair.After.DayOfWeek).ToArray() }));

        if (!await store.TrySaveUpdateAsync(branch, auditLog, cancellationToken))
        {
            return new UpdateBusinessHoursResult.Stale();
        }

        return new UpdateBusinessHoursResult.Succeeded(ToView(branch, days));
    }

    private static BusinessHoursView ToView(Domain.Branches.Branch branch, IReadOnlyList<BusinessHoursDayView> days) =>
        new(branch.Id, branch.Name, branch.Timezone, branch.UpdatedAt, true, days);

    private static string Serialize(IEnumerable<BusinessHoursDayView> days) =>
        JsonSerializer.Serialize(days.ToDictionary(
            day => day.DayOfWeek,
            day => new Dictionary<string, object?>
            {
                ["isOpen"] = day.IsOpen,
                ["openingTime"] = day.OpeningTime,
                ["closingTime"] = day.ClosingTime,
            },
            StringComparer.Ordinal));

    // PostgreSQL keeps microseconds: tokens compare and round-trip at that precision.
    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % 10), TimeSpan.Zero);
}
