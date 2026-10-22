using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace FieldOps.Application.Features.Customers;

/// <summary>The event types of the Activity tab (BR-10) in display order.</summary>
public static class CustomerActivityTypes
{
    public const string All = "all";

    public const string Customer = "customer";

    public const string Requests = "requests";

    public const string Messages = "messages";

    public const string Quotes = "quotes";

    public const string Jobs = "jobs";

    public const string Visits = "visits";

    public const string Invoices = "invoices";

    public const string Payments = "payments";

    public static IReadOnlyList<string> Ordered { get; } =
        [Customer, Requests, Messages, Quotes, Jobs, Visits, Invoices, Payments];
}

/// <summary>The link kinds of an activity event (BR-11).</summary>
public static class CustomerActivityLinks
{
    public const string Request = "request";

    public const string Quote = "quote";

    public const string Job = "job";

    public const string Invoice = "invoice";
}

/// <summary>One whitelisted audit action: its type, the audit entity type that carries it and its link kind.</summary>
public sealed record CustomerActivityAction(string Action, string Type, string EntityType, string? LinkKind);

/// <summary>
/// The single whitelist of the Activity tab (BR-10, BR-11): which audit actions are shown, under which type, from which
/// audit entity type and with which link. An action that is not listed here never leaves the database.
/// </summary>
public static class CustomerActivityCatalog
{
    public const string WorkOrderStatusChanged = "work_order.status_changed";

    public const string VisitDispatched = "visit.dispatched";

    public const string PaymentRecorded = "payment.recorded";

    public const string PaymentRefunded = "payment.refunded";

    public const string MessageSent = "portal.message_sent";

    public const string ServiceRequestEntity = "service_request";

    public const string QuoteEntity = "quote";

    public const string WorkOrderEntity = "work_order";

    public const string VisitEntity = "visit";

    public const string InvoiceEntity = "invoice";

    public const string PaymentEntity = "payment";

    public const string ContactEntity = "customer_contact";

    public static IReadOnlyList<CustomerActivityAction> Entries { get; } = Build();

    private static readonly Dictionary<string, CustomerActivityAction> ByAction =
        Entries.ToDictionary(entry => entry.Action, StringComparer.Ordinal);

    public static bool TryGet(string action, out CustomerActivityAction entry) => ByAction.TryGetValue(action, out entry!);

    /// <summary>True for the property actions, whose summary carries the property name.</summary>
    public static bool NamesProperty(string action) =>
        ByAction.TryGetValue(action, out var entry) && entry.EntityType == CustomerAuditActions.PropertyEntityType;

    public static IReadOnlyList<CustomerActivityAction> Of(string type, string entityType) =>
        [.. Entries.Where(entry => entry.Type == type && entry.EntityType == entityType)];

    public static IReadOnlyList<string> EntityTypesOf(string type) =>
        [.. Entries.Where(entry => entry.Type == type).Select(entry => entry.EntityType).Distinct()];

    /// <summary>Customer and Messages need only Customers read; the others need their module read policy (BR-10).</summary>
    public static bool IsAvailable(string type, CustomerRecordAccess access) =>
        type switch
        {
            CustomerActivityTypes.Requests or CustomerActivityTypes.Quotes => access.Requests,
            CustomerActivityTypes.Jobs or CustomerActivityTypes.Visits => access.Jobs,
            CustomerActivityTypes.Invoices or CustomerActivityTypes.Payments => access.Invoices,
            _ => true,
        };

    public static IReadOnlyList<string> AvailableTypes(CustomerRecordAccess access) =>
        [.. CustomerActivityTypes.Ordered.Where(type => IsAvailable(type, access))];

    private static List<CustomerActivityAction> Build()
    {
        var entries = new List<CustomerActivityAction>();

        void Add(string type, string entityType, string? link, params string[] actions) =>
            entries.AddRange(actions.Select(action => new CustomerActivityAction(action, type, entityType, link)));

        Add(
            CustomerActivityTypes.Customer,
            CustomerAuditActions.EntityType,
            null,
            CustomerAuditActions.Created,
            CustomerAuditActions.Updated,
            CustomerAuditActions.Archived,
            CustomerAuditActions.Reactivated);
        Add(
            CustomerActivityTypes.Customer,
            CustomerAuditActions.PropertyEntityType,
            null,
            CustomerAuditActions.PropertyCreated,
            CustomerAuditActions.PropertyUpdated,
            CustomerAuditActions.PropertyPrimaryChanged,
            CustomerAuditActions.PropertyArchived,
            CustomerAuditActions.PropertyReactivated);
        Add(CustomerActivityTypes.Customer, CustomerAuditActions.NoteEntityType, null, CustomerAuditActions.NoteCreated);
        Add(
            CustomerActivityTypes.Requests,
            ServiceRequestEntity,
            CustomerActivityLinks.Request,
            "service_request.created",
            "service_request.assessment_scheduled",
            "service_request.assessment_rescheduled",
            "service_request.assessment_cancelled",
            "service_request.assessment_completed",
            "service_request.information_requested",
            "service_request.customer_response_logged",
            "service_request.quoted");
        Add(CustomerActivityTypes.Messages, ContactEntity, null, MessageSent);
        Add(
            CustomerActivityTypes.Quotes,
            QuoteEntity,
            CustomerActivityLinks.Quote,
            "quote.sent",
            "quote.revised",
            "quote.approved",
            "quote.rejected",
            "quote.clarification_requested",
            "quote.expired",
            "quote.cancelled");
        Add(CustomerActivityTypes.Jobs, WorkOrderEntity, CustomerActivityLinks.Job, "work_order.created", WorkOrderStatusChanged);
        Add(
            CustomerActivityTypes.Visits,
            VisitEntity,
            CustomerActivityLinks.Job,
            VisitDispatched,
            "visit.completed",
            "visit.cancelled",
            "visit.reschedule_requested");
        Add(
            CustomerActivityTypes.Invoices,
            InvoiceEntity,
            CustomerActivityLinks.Invoice,
            "invoice.sent",
            "invoice.email_resent",
            "invoice.bank_transfer_reported");
        Add(CustomerActivityTypes.Payments, PaymentEntity, CustomerActivityLinks.Invoice, PaymentRecorded, PaymentRefunded);

        return entries;
    }
}

/// <summary>Validation of the activity query (BR-12): type, rolling period and the opaque cursor.</summary>
public static class CustomerActivityRules
{
    public const int BlockSize = 20;

    public const string TypeInvalid = "Choose an available activity type.";

    public const string PeriodInvalid = "Choose a valid period.";

    public const string BeforeInvalid = "The cursor is not valid.";

    public static bool TryParsePeriod(string? text, DateTimeOffset now, out DateTimeOffset? since)
    {
        since = null;

        switch (string.IsNullOrEmpty(text) ? "last_90_days" : text)
        {
            case "last_7_days":
                since = now.AddDays(-7);

                return true;
            case "last_30_days":
                since = now.AddDays(-30);

                return true;
            case "last_90_days":
                since = now.AddDays(-90);

                return true;
            case "last_12_months":
                since = now.AddMonths(-12);

                return true;
            case "all":
                return true;
            default:
                return false;
        }
    }

    /// <summary>Resolves the requested types: blank or <c>all</c> is every available type; otherwise one available type.</summary>
    public static bool TryParseTypes(string? text, CustomerRecordAccess access, out IReadOnlyList<string> types)
    {
        var available = CustomerActivityCatalog.AvailableTypes(access);
        types = available;

        if (string.IsNullOrEmpty(text) || text == CustomerActivityTypes.All)
        {
            return true;
        }

        if (available.Contains(text, StringComparer.Ordinal))
        {
            types = [text];

            return true;
        }

        return false;
    }

    public static string EncodeCursor(CustomerActivityCursor cursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{cursor.OccurredAt.UtcTicks}.{cursor.Id}")));

    public static bool TryDecodeCursor(string text, out CustomerActivityCursor? cursor)
    {
        cursor = null;

        try
        {
            var decoded = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text));
            var parts = decoded.Split('.');

            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && ticks <= DateTime.MaxValue.Ticks)
            {
                cursor = new CustomerActivityCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);

                return true;
            }
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or DecoderFallbackException)
        {
            return false;
        }

        return false;
    }
}
