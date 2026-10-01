using System.Net;
using System.Text.Json;
using FieldOps.Application.Auditing;
using FieldOps.Application.Features.Customers;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Notifications;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Audit rows of property and note mutations (BR-21). They carry ids, flags and field names only: never
/// addresses, instructions, names or note text.
/// </summary>
internal static class PropertyAudit
{
    public static AuditLog Created(Property property, Guid actorUserId, IPAddress? clientIp)
    {
        var (_, after) = AuditFieldDiff.ForCreate(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["branchId"] = property.BranchId,
            ["isPrimary"] = property.IsPrimary,
        });

        return For(property, CustomerAuditActions.PropertyCreated, actorUserId, clientIp, afterData: after);
    }

    public static AuditLog Updated(Property property, IReadOnlyList<string> changedFields, Guid actorUserId, IPAddress? clientIp) =>
        For(
            property,
            CustomerAuditActions.PropertyUpdated,
            actorUserId,
            clientIp,
            metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["changedFields"] = changedFields,
            }));

    public static AuditLog PrimaryChanged(Property newPrimary, Guid? previousPrimaryId, Guid actorUserId, IPAddress? clientIp) =>
        For(
            newPrimary,
            CustomerAuditActions.PropertyPrimaryChanged,
            actorUserId,
            clientIp,
            beforeData: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["primaryPropertyId"] = previousPrimaryId,
            }),
            afterData: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["primaryPropertyId"] = newPrimary.Id,
            }));

    public static AuditLog StateChanged(Property property, bool isActive, Guid actorUserId, IPAddress? clientIp)
    {
        var (before, after) = AuditFieldDiff.ForStateChange(!isActive, isActive);

        return For(
            property,
            isActive ? CustomerAuditActions.PropertyReactivated : CustomerAuditActions.PropertyArchived,
            actorUserId,
            clientIp,
            beforeData: before,
            afterData: after);
    }

    public static AuditLog NoteCreated(CustomerNote note, Guid customerBranchId, Guid actorUserId, IPAddress? clientIp) =>
        AuditLog.Create(
            note.OrganizationId,
            CustomerAuditActions.NoteCreated,
            CustomerAuditActions.NoteEntityType,
            actorUserId: actorUserId,
            entityId: note.Id,
            branchId: customerBranchId,
            ipAddress: clientIp);

    private static AuditLog For(
        Property property,
        string action,
        Guid actorUserId,
        IPAddress? clientIp,
        string? beforeData = null,
        string? afterData = null,
        string? metadata = null) =>
        AuditLog.Create(
            property.OrganizationId,
            action,
            CustomerAuditActions.PropertyEntityType,
            actorUserId: actorUserId,
            entityId: property.Id,
            branchId: property.BranchId,
            ipAddress: clientIp,
            beforeData: beforeData,
            afterData: afterData,
            metadata: metadata);
}
