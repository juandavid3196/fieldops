namespace FieldOps.Api.Authorization;

/// <summary>Role-based authorization policy of the notifications inbox (notifications-center BR-01).</summary>
public static class NotificationInboxPolicies
{
    /// <summary><c>owner</c>, <c>operations_manager</c>, <c>dispatcher</c>, <c>accounting</c>, <c>viewer</c>: every inbox endpoint.</summary>
    public const string Inbox = "NotificationInbox";
}
