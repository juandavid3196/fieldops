namespace FieldOps.Domain.Messaging;

public static class ConversationTypes
{
    public const string Job = "job";

    public const string Team = "team";
}

/// <summary>
/// An internal conversation: one Job conversation per work order and one Team conversation per branch (technician-messages SA-01).
/// <c>BranchId</c> is the branch at creation time; participation is computed from the work order and branch, never stored.
/// </summary>
public sealed class Conversation
{
    private Conversation()
    {
    }

    private Conversation(Guid id, Guid organizationId, Guid branchId, string type, Guid? workOrderId, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        Type = type;
        WorkOrderId = workOrderId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid BranchId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public Guid? WorkOrderId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastMessageAt { get; private set; }

    public static Conversation CreateJob(Guid organizationId, Guid branchId, Guid workOrderId, DateTimeOffset now)
    {
        RequireIds(organizationId, branchId);

        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException("Work order id is required.", nameof(workOrderId));
        }

        return new Conversation(Guid.NewGuid(), organizationId, branchId, ConversationTypes.Job, workOrderId, now);
    }

    public static Conversation CreateTeam(Guid organizationId, Guid branchId, DateTimeOffset now)
    {
        RequireIds(organizationId, branchId);

        return new Conversation(Guid.NewGuid(), organizationId, branchId, ConversationTypes.Team, null, now);
    }

    private static void RequireIds(Guid organizationId, Guid branchId)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id is required.", nameof(organizationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("Branch id is required.", nameof(branchId));
        }
    }
}
