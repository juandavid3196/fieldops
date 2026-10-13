using FieldOps.Application.Features.PortalAccess;

namespace FieldOps.Application.Features.PortalDashboard;

/// <summary>Customer portal API contracts (customer portal API contracts table). Dates are organization-local days, times are HH:mm.</summary>
public sealed record PortalOrganization(string Name, string? Phone, bool CanReceiveMessages);

public sealed record PortalPropertySummary(
    Guid Id,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? State,
    string? PostalCode,
    bool IsPrimary,
    DateOnly? LastServiceOn);

public sealed record PortalActionQuote(
    Guid Id,
    string DisplayNumber,
    string Scope,
    decimal Total,
    string Currency,
    DateOnly? ValidUntil,
    string Status,
    int MoreCount);

public sealed record PortalPaymentDue(
    Guid Id,
    string DisplayNumber,
    string Title,
    decimal BalanceDue,
    string Currency,
    DateOnly? DueDate,
    bool IsOverdue,
    int MoreCount);

public sealed record PortalArrivalWindow(string Start, string End);

public sealed record PortalTechnician(string FullName, string FirstName);

public sealed record PortalAppointmentProperty(string Name, string AddressLine1);

public sealed record PortalAppointment(
    Guid VisitId,
    Guid WorkOrderId,
    string WorkOrderNumber,
    string Title,
    DateOnly Date,
    string StartTime,
    string EndTime,
    PortalArrivalWindow? ArrivalWindow,
    PortalTechnician? Technician,
    PortalAppointmentProperty Property,
    string Status,
    string ProgressStep,
    bool CanRequestReschedule,
    DateOnly? RescheduleRequestedOn,
    bool ReportAvailable);

public sealed record PortalRequestRow(
    Guid Id,
    string DisplayNumber,
    string Title,
    string? PropertyName,
    string Status,
    string StatusLabel,
    DateOnly SubmittedOn);

public sealed record PortalUpdateTarget(string Kind, Guid Id);

public sealed record PortalUpdate(string Type, string Title, string Subtitle, DateTimeOffset OccurredAt, bool IsUnread, PortalUpdateTarget Target);

public sealed record PortalUpdates(IReadOnlyList<PortalUpdate> Items, int UnreadCount);

public sealed record PortalActivityRow(
    Guid WorkOrderId,
    string Title,
    DateOnly CompletedOn,
    string Reference,
    decimal? Amount,
    string Currency,
    string Status,
    Guid? InvoiceId,
    Guid? ReceiptPaymentId);

public sealed record PortalDashboard(
    PortalOrganization Organization,
    IReadOnlyList<PortalPropertySummary> Properties,
    Guid? SelectedPropertyId,
    PortalActionQuote? ActionQuote,
    PortalPaymentDue? PaymentDue,
    PortalAppointment? UpcomingAppointment,
    IReadOnlyList<PortalRequestRow> ActiveRequests,
    PortalUpdates Updates,
    IReadOnlyList<PortalActivityRow> RecentActivity);

public sealed record PortalQuoteRow(
    Guid Id,
    string DisplayNumber,
    string Scope,
    string? PropertyName,
    decimal Total,
    string Currency,
    string Status,
    DateOnly? ValidUntil);

public sealed record PortalInvoiceRow(
    Guid Id,
    string DisplayNumber,
    string Title,
    DateOnly? IssueDate,
    DateOnly? DueDate,
    decimal Total,
    decimal BalanceDue,
    string Currency,
    string Status);

public sealed record PortalLinkedQuote(Guid Id, string DisplayNumber, string Status);

public sealed record PortalRequestProperty(
    Guid Id,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? StateRegion,
    string? PostalCode,
    string CountryCode,
    string? AccessInstructions,
    bool IsPrimary);

public sealed record PortalAvailabilityDate(DateOnly Date, string Window);

public sealed record PortalRequestAvailability(string Mode, IReadOnlyList<PortalAvailabilityDate> Dates, string Window, string? Notes);

public sealed record PortalRequestDetail(
    Guid Id,
    string DisplayNumber,
    string Title,
    string? PropertyName,
    string Status,
    string StatusLabel,
    DateOnly SubmittedOn,
    string CategoryName,
    string? ServiceName,
    string Description,
    string Urgency,
    bool HasActiveDamage,
    PortalRequestProperty? Property,
    PortalRequestAvailability? Availability,
    PortalLinkedQuote? Quote);

public sealed record PortalProperty(
    Guid Id,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? StateRegion,
    string? PostalCode,
    string CountryCode,
    string? AccessInstructions,
    bool IsPrimary,
    DateOnly? LastServiceOn,
    DateTimeOffset UpdatedAt);

public sealed record PortalRescheduleRequested(DateOnly RequestedOn);

/// <summary>The fields of a reschedule request body, still unvalidated.</summary>
public sealed record PortalRescheduleInput(string? PreferredDate, string? TimeWindow, string? Reason);

public sealed record PortalRescheduleValues(DateOnly PreferredDate, string TimeWindow, string Reason);

public sealed record PortalPropertyCreateInput(
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? CountryCode,
    string? AccessInstructions);

public sealed record PortalPropertyEditInput(
    bool HasName,
    string? Name,
    bool HasAccessInstructions,
    string? AccessInstructions,
    bool? IsPrimary,
    string? UpdatedAt);

public static class PortalPropertyMessages
{
    public const string NameRequired = "Enter a property name.";

    public const string NameTooLong = "Name must be 140 characters or fewer.";

    public const string InstructionsTooLong = "Access instructions must be 1,000 characters or fewer.";

    public const string CountryMessage = "Only US properties are supported.";

    public const string AddressChangeMessage = "The address can't be changed online.";

    public const string UpdatedAtMessage = "Reload the property and try again.";
}

/// <summary>The kind of an appointment list (customer portal BR-29).</summary>
public enum PortalAppointmentScope
{
    Upcoming,
    Past,
}

/// <summary>The recipient of a message to the organization (customer portal BR-35).</summary>
public sealed record PortalMessageTarget(
    string OrganizationName,
    string? RecipientEmail,
    string CustomerName,
    string ContactName,
    string? ContactEmail,
    string? ContactPhone);

public sealed record PortalRescheduleEmailData(
    string? RecipientEmail,
    string WorkOrderNumber,
    string CustomerName,
    DateOnly CurrentDate,
    string CurrentWindow,
    DateOnly PreferredDate,
    string PreferredWindow,
    string Reason);

public abstract record PortalRescheduleResult
{
    private PortalRescheduleResult()
    {
    }

    public sealed record NotFound : PortalRescheduleResult;

    public sealed record NotEligible : PortalRescheduleResult;

    public sealed record AlreadyRequested : PortalRescheduleResult;

    public sealed record Created(DateOnly RequestedOn, PortalRescheduleEmailData Email) : PortalRescheduleResult;
}

public abstract record PortalPropertyEditResult
{
    private PortalPropertyEditResult()
    {
    }

    public sealed record NotFound : PortalPropertyEditResult;

    public sealed record Changed : PortalPropertyEditResult;

    public sealed record Saved(PortalProperty Property) : PortalPropertyEditResult;
}

/// <summary>Reads and writes of the customer portal, always starting from the validated session scope (customer portal BR-37).</summary>
public interface IPortalDashboardStore
{
    /// <summary>Null when <paramref name="propertyId"/> is not an active property of the customer (404).</summary>
    Task<PortalDashboard?> GetDashboardAsync(PortalScope scope, Guid? propertyId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PortalUpdates> ListUpdatesAsync(PortalScope scope, DateTimeOffset now, CancellationToken cancellationToken);

    Task MarkUpdatesSeenAsync(PortalScope scope, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Null when <paramref name="propertyId"/> is not an active property of the customer (404).</summary>
    Task<PortalPage<PortalActivityRow>?> ListActivityAsync(
        PortalScope scope, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IPortalCatalogStore
{
    Task<PortalPage<PortalRequestRow>?> ListRequestsAsync(PortalScope scope, Guid? propertyId, int page, CancellationToken cancellationToken);

    Task<PortalRequestDetail?> GetRequestAsync(PortalScope scope, Guid requestId, CancellationToken cancellationToken);

    Task<PortalPage<PortalQuoteRow>?> ListQuotesAsync(
        PortalScope scope, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PortalPage<PortalInvoiceRow>?> ListInvoicesAsync(PortalScope scope, Guid? propertyId, int page, CancellationToken cancellationToken);

    Task<FieldOps.Application.Features.QuoteLinks.PublicBinary?> GetOrganizationLogoAsync(PortalScope scope, CancellationToken cancellationToken);

    Task<FieldOps.Application.Features.OnlinePayments.CompletionReportSource?> GetWorkOrderReportAsync(
        PortalScope scope, Guid workOrderId, CancellationToken cancellationToken);
}

public interface IPortalAppointmentStore
{
    Task<PortalPage<PortalAppointment>?> ListAsync(
        PortalScope scope, PortalAppointmentScope kind, Guid? propertyId, int page, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PortalAppointment?> GetAsync(PortalScope scope, Guid visitId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<string> GetTimezoneAsync(PortalScope scope, CancellationToken cancellationToken);

    /// <summary>Locks the visit, rechecks the eligibility and stores the request in one transaction (customer portal BR-32).</summary>
    Task<PortalRescheduleResult> RequestRescheduleAsync(
        PortalScope scope,
        Guid visitId,
        PortalRescheduleValues values,
        System.Net.IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IPortalPropertyStore
{
    Task<IReadOnlyList<PortalProperty>> ListAsync(PortalScope scope, CancellationToken cancellationToken);

    Task<PortalProperty> CreateAsync(
        PortalScope scope, PortalPropertyCreateInput values, System.Net.IPAddress? clientIp, DateTimeOffset now, CancellationToken cancellationToken);

    Task<PortalPropertyEditResult> UpdateAsync(
        PortalScope scope,
        Guid propertyId,
        PortalPropertyEditInput input,
        DateTimeOffset expectedUpdatedAt,
        System.Net.IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IPortalMessageStore
{
    /// <summary>The organization recipient and contact data; the recipient is null when no email is configured.</summary>
    Task<PortalMessageTarget> GetTargetAsync(PortalScope scope, CancellationToken cancellationToken);

    Task WriteMessageAuditAsync(PortalScope scope, int length, System.Net.IPAddress? clientIp, CancellationToken cancellationToken);
}

public interface IPortalNotifier
{
    Task SendMessageAsync(PortalMessageTarget target, string message, string organizationName, CancellationToken cancellationToken);

    Task SendRescheduleAsync(PortalRescheduleEmailData email, CancellationToken cancellationToken);
}
