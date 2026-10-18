namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policy of technician-messages (BR-01).</summary>
public static class MessagingPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>technician</c>.</summary>
    public const string Access = "Messaging";
}
