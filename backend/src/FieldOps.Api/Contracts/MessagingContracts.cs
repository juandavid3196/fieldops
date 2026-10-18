namespace FieldOps.Api.Contracts;

/// <summary>POST /messages/conversations/resolve (technician-messages BR-11): <c>job</c> with a work order or <c>team</c> with a branch.</summary>
public sealed record ResolveConversationBody(string? Type, Guid? WorkOrderId, Guid? BranchId);

/// <summary>POST /messages/conversations/{id}/messages (BR-09); validated by the use case after authorization.</summary>
public sealed record SendConversationMessageBody(string? ClientMessageId, string? Body);
