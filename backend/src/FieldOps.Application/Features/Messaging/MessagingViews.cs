using FieldOps.Application.Features.TechnicianVisits;

namespace FieldOps.Application.Features.Messaging;

/// <summary>The caller of every messaging use case; the three identifiers come from the session ticket only.</summary>
public sealed record MessagingActor(Guid OrganizationId, Guid MembershipId, Guid UserId);

public sealed record MessagingSummary(int UnreadMessages, int UnreadConversations);

public sealed record ConversationWorkOrderRef(Guid Id, string DisplayNumber);

public sealed record LastMessageView(string Preview, string AuthorName, string AuthorInitials, bool IsMine, DateTimeOffset CreatedAt);

/// <summary><c>Id</c> is null for the virtual Team entry of a branch whose conversation does not exist yet (BR-04).</summary>
public sealed record ConversationSummary(
    Guid? Id,
    string Type,
    string Title,
    Guid BranchId,
    ConversationWorkOrderRef? WorkOrder,
    LastMessageView? LastMessage,
    int UnreadCount);

public sealed record ConversationList(IReadOnlyList<ConversationSummary> Items, string? NextCursor);

public sealed record ConversationDetailWorkOrder(Guid Id, string DisplayNumber, string Title, Guid? VisitId);

public sealed record ConversationDetail(
    Guid Id, string Type, string Title, string BranchName, ConversationDetailWorkOrder? WorkOrder);

public sealed record MessageAuthor(string Name, string Initials, string RoleLabel);

/// <summary><c>ClientMessageId</c> is returned to its author only.</summary>
public sealed record MessageView(Guid Id, Guid? ClientMessageId, MessageAuthor Author, bool IsMine, string Body, DateTimeOffset CreatedAt);

public sealed record MessagePage(IReadOnlyList<MessageView> Items, bool HasMore);

public sealed record ResolvedConversation(Guid ConversationId);

public sealed record SentMessage(MessageView Message, bool Created);

/// <summary>The ordering key of the list: last message (nulls last), lower-cased title (ordinal), then the row key.</summary>
public sealed record ConversationListCursor(DateTimeOffset? LastMessageAt, string SortTitle, Guid Key);

public sealed record ConversationPage(IReadOnlyList<ConversationSummary> Items, ConversationListCursor? Next);

public sealed record ConversationQuery(string Filter, string? Search, ConversationListCursor? After);

public sealed record ResolveTarget(string Type, Guid TargetId);

public enum MessagingStatus
{
    Ok,
    NotFound,
    ProfileNotLinked,
    TechnicianInactive,
    InvalidCursor,
}

/// <summary>Outcome of a store call: every non-participant, foreign or unknown target is the identical <see cref="MessagingStatus.NotFound"/>.</summary>
public sealed record MessagingStoreResult<T>(MessagingStatus Status, T? Value = default)
{
    public static MessagingStoreResult<T> Ok(T value) => new(MessagingStatus.Ok, value);

    public static MessagingStoreResult<T> Failure(MessagingStatus status) => new(status);
}

public enum MessagingResultKind
{
    Succeeded,
    NotFound,
    Forbidden,
    Invalid,
    RateLimited,
}

public sealed record MessagingResult<T>(
    MessagingResultKind Kind,
    T? Value = default,
    string? Code = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    TimeSpan? RetryAfter = null)
{
    public static MessagingResult<T> Ok(T value) => new(MessagingResultKind.Succeeded, value);

    public static MessagingResult<T> NotFound(string? code = null) => new(MessagingResultKind.NotFound, Code: code);

    public static MessagingResult<T> Forbidden(string code) => new(MessagingResultKind.Forbidden, Code: code);

    public static MessagingResult<T> Invalid(string field, string message) =>
        new(MessagingResultKind.Invalid, Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });

    public static MessagingResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(MessagingResultKind.Invalid, Errors: errors);

    public static MessagingResult<T> RateLimited(TimeSpan retryAfter) =>
        new(MessagingResultKind.RateLimited, Code: MessagingCodes.RateLimited, RetryAfter: retryAfter);

    internal static MessagingResult<T> From(MessagingStoreResult<T> result) =>
        result.Status switch
        {
            MessagingStatus.Ok => Ok(result.Value!),
            MessagingStatus.ProfileNotLinked => NotFound(TechnicianVisitCodes.ProfileNotLinked),
            MessagingStatus.TechnicianInactive => Forbidden(TechnicianVisitCodes.TechnicianInactive),
            MessagingStatus.InvalidCursor => Invalid(MessagingFields.Cursor, MessagingMessages.CursorInvalid),
            _ => NotFound(),
        };
}

public static class MessagingCodes
{
    public const string RateLimited = "message_rate_limited";
}

public static class MessagingFields
{
    public const string Filter = "filter";

    public const string Search = "search";

    public const string Cursor = "cursor";

    public const string Type = "type";

    public const string Body = "body";

    public const string ClientMessageId = "clientMessageId";
}

public static class MessagingMessages
{
    public const string NotAvailable = "This conversation isn't available.";

    public const string RateLimited = "You're sending messages too quickly. Try again in a moment.";

    public const string FilterInvalid = "Choose a valid filter.";

    public const string SearchInvalid = "Enter 2 to 80 characters.";

    public const string CursorInvalid = "The page cursor isn't valid.";

    public const string TypeInvalid = "Choose a job or team conversation.";

    public const string BodyInvalid = "Enter a message of up to 2000 characters.";

    public const string ClientMessageIdInvalid = "Enter a valid message id.";

    /// <summary>The safe ProblemDetails title for a code; a null code is the identical 404.</summary>
    public static string Title(string? code) => code switch
    {
        MessagingCodes.RateLimited => RateLimited,
        TechnicianVisitCodes.ProfileNotLinked or TechnicianVisitCodes.TechnicianInactive => TechnicianVisitMessages.Title(code),
        _ => NotAvailable,
    };
}

/// <summary>Roles allowed by the Messaging policy (Actors and permissions).</summary>
public static class MessagingCatalog
{
    public static IReadOnlyList<string> Roles { get; } = ["owner", "operations_manager", "dispatcher", "technician"];
}
