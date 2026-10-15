namespace FieldOps.Application.Features.Notifications;

/// <summary>The notification event codes of notifications-center BR-05 (stored as <c>template_code</c>).</summary>
public static class NotificationEventCodes
{
    public const string RequestCreated = "request.created";

    public const string CustomerMessageSent = "customer.message_sent";

    public const string QuoteApproved = "quote.approved";

    public const string QuoteRejected = "quote.rejected";

    public const string QuoteClarificationRequested = "quote.clarification_requested";

    public const string VisitAssigned = "visit.assigned";

    public const string VisitOnTheWay = "visit.on_the_way";

    public const string VisitStarted = "visit.started";

    public const string VisitPaused = "visit.paused";

    public const string JobCompleted = "job.completed";

    public const string InvoiceSent = "invoice.sent";

    public const string InvoiceBankTransferReported = "invoice.bank_transfer_reported";

    public const string PaymentReceived = "payment.received";
}

/// <summary>The entity kinds of the payload and the link targets (BR-06, BR-12).</summary>
public static class NotificationEntityKinds
{
    public const string Request = "request";

    public const string Customer = "customer";

    public const string Quote = "quote";

    public const string Visit = "visit";

    public const string WorkOrder = "work_order";

    public const string Invoice = "invoice";
}

public sealed record NotificationCatalogEntry(string Code, string Type, IReadOnlyList<string> Roles);

/// <summary>
/// Single source of the event code to type and recipient role set mapping (BR-02, BR-04). The same table drives the
/// write-time recipient fan-out and the read-time visibility filter (BR-03), so they cannot drift.
/// </summary>
public static class NotificationCatalog
{
    private static readonly string[] Managers = ["owner", "dispatcher"];

    private static readonly string[] Operations = ["owner", "operations_manager", "dispatcher"];

    private static readonly string[] OperationsAndBilling = ["owner", "operations_manager", "dispatcher", "accounting"];

    private static readonly string[] Billing = ["owner", "accounting"];

    private static readonly string[] AllInternal = ["owner", "operations_manager", "dispatcher", "accounting", "viewer"];

    private static readonly string[] ScheduleReaders = ["owner", "operations_manager", "dispatcher", "viewer"];

    private static readonly Dictionary<string, NotificationCatalogEntry> Entries = Build(
        new(NotificationEventCodes.RequestCreated, "request", Managers),
        new(NotificationEventCodes.CustomerMessageSent, "message", Managers),
        new(NotificationEventCodes.QuoteApproved, "quote", Managers),
        new(NotificationEventCodes.QuoteRejected, "quote", Managers),
        new(NotificationEventCodes.QuoteClarificationRequested, "quote", Managers),
        new(NotificationEventCodes.VisitAssigned, "visit", Operations),
        new(NotificationEventCodes.VisitOnTheWay, "visit", Operations),
        new(NotificationEventCodes.VisitStarted, "visit", Operations),
        new(NotificationEventCodes.VisitPaused, "visit", Operations),
        new(NotificationEventCodes.JobCompleted, "job", OperationsAndBilling),
        new(NotificationEventCodes.InvoiceSent, "invoice", Billing),
        new(NotificationEventCodes.InvoiceBankTransferReported, "invoice", Billing),
        new(NotificationEventCodes.PaymentReceived, "payment", Billing));

    /// <summary>Read policy roles per entity kind (BR-12): the policy of the destination screen.</summary>
    private static readonly Dictionary<string, string[]> KindReaders = new(StringComparer.Ordinal)
    {
        [NotificationEntityKinds.Request] = AllInternal,
        [NotificationEntityKinds.Customer] = AllInternal,
        [NotificationEntityKinds.Quote] = AllInternal,
        [NotificationEntityKinds.Visit] = ScheduleReaders,
        [NotificationEntityKinds.WorkOrder] = ScheduleReaders,
        [NotificationEntityKinds.Invoice] = AllInternal,
    };

    /// <summary>The Notification Inbox policy roles (BR-01).</summary>
    public static IReadOnlyList<string> InboxRoles => AllInternal;

    public static IReadOnlyList<string> Types { get; } = ["request", "message", "quote", "visit", "job", "invoice", "payment"];

    public static IReadOnlyCollection<string> Codes => Entries.Keys;

    public static bool TryGet(string code, out NotificationCatalogEntry entry) => Entries.TryGetValue(code, out entry!);

    public static NotificationCatalogEntry Get(string code) =>
        Entries.TryGetValue(code, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown notification event code.");

    /// <summary>The event codes whose role set includes <paramref name="role"/>.</summary>
    public static string[] CodesForRole(string role) =>
        [.. Entries.Values.Where(entry => entry.Roles.Contains(role, StringComparer.Ordinal)).Select(entry => entry.Code)];

    public static string[] CodesForType(string type) =>
        [.. Entries.Values.Where(entry => string.Equals(entry.Type, type, StringComparison.Ordinal)).Select(entry => entry.Code)];

    public static bool CanReadKind(string role, string kind) =>
        KindReaders.TryGetValue(kind, out var roles) && roles.Contains(role, StringComparer.Ordinal);

    private static Dictionary<string, NotificationCatalogEntry> Build(params NotificationCatalogEntry[] entries) =>
        entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);
}
