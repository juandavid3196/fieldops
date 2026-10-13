using System.Text;

namespace FieldOps.Application.Features.Customers;

public sealed record CustomerDetailSummary(
    int TotalJobs,
    decimal LifetimeValue,
    DateTimeOffset CustomerSince,
    DateTimeOffset? LastServiceAt);

public sealed record CustomerLastInvoice(string Number, string Status, DateOnly? IssueDate);

/// <summary>Response of GET /customers/{id}/detail (FR-02).</summary>
public sealed record CustomerOverview(
    Guid Id,
    string Type,
    string DisplayName,
    CustomerOverviewContact Contact,
    string Lifecycle,
    string DisplayStatus,
    bool IsActive,
    decimal OutstandingBalance,
    string Currency,
    string Timezone,
    CustomerDetailSummary Summary,
    CustomerLastInvoice? LastInvoice,
    IReadOnlyList<CustomerTagView> Tags,
    string? PinnedNote);

/// <summary>The contact of the overview: the contact details plus the portal status of the primary contact (customer portal BR-10).</summary>
public sealed record CustomerOverviewContact(
    string FirstName,
    string LastName,
    string? Title,
    string Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms,
    string PortalStatus,
    DateOnly? PortalLinkedOn,
    DateOnly? InvitationExpiresOn);

/// <summary>Values the store derives for the overview; the handler adds the contact, tags and status.</summary>
public sealed record CustomerOverviewData(
    string DisplayName,
    decimal Balance,
    int TotalJobs,
    decimal LifetimeValue,
    DateTimeOffset CustomerSince,
    DateTimeOffset? LastServiceAt,
    CustomerLastInvoice? LastInvoice);

public sealed record CustomerPropertyBranch(Guid Id, string Name);

public sealed record CustomerPropertyView(
    Guid Id,
    string Name,
    bool IsPrimary,
    bool IsActive,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? StateRegion,
    string? PostalCode,
    CustomerPropertyBranch? Branch,
    string? ServiceInstructions,
    CustomerLastService? LastService,
    CustomerNextService? NextAppointment);

public sealed record CustomerPropertyList(IReadOnlyList<CustomerPropertyView> Items, string Timezone);

/// <summary>Property fields after validation (BR-05, BR-06); the branch is verified by the handler.</summary>
public sealed record CustomerPropertyValues(
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? StateRegion,
    string? PostalCode,
    Guid BranchId,
    string? ServiceInstructions);

/// <summary>Unvalidated property fields of a POST or PUT body.</summary>
public sealed record CustomerPropertyInput(
    string? Name,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? StateRegion,
    string? PostalCode,
    string? BranchId,
    string? ServiceInstructions);

/// <summary>What a property edit needs before validation: its current branch and whether it is active.</summary>
public sealed record CustomerPropertyState(Guid? BranchId, bool IsActive);

public enum CustomerPropertyOutcome
{
    NotFound,
    Changed,
    AlreadyPrimary,
    ArchivedCannotBePrimary,
    PrimaryCannotBeArchived,
    AlreadyArchived,
    AlreadyActive,
    ArchivedCannotBeEdited,
    PrimaryChangedConcurrently,
}

public sealed record CustomerPropertyChange(CustomerPropertyOutcome Outcome, CustomerPropertyView? Property = null);

public enum CustomerPropertyAction
{
    SetPrimary,
    Archive,
    Reactivate,
}

public sealed record CustomerRecentWorkItem(
    string Type,
    Guid Id,
    string Number,
    string? Title,
    string Status,
    DateTimeOffset Date,
    string? TechnicianName,
    decimal? Amount);

public sealed record CustomerRecentWork(IReadOnlyList<CustomerRecentWorkItem> Items, string Currency, string Timezone);

public sealed record CustomerUpcomingAppointment(
    Guid VisitId,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string JobNumber,
    string? JobTitle,
    string PropertyName,
    string? TechnicianName);

public sealed record CustomerUpcomingAppointments(IReadOnlyList<CustomerUpcomingAppointment> Items, string Timezone);

public sealed record CustomerNoteView(Guid Id, string Note, string AuthorName, DateTimeOffset CreatedAt);

public sealed record CustomerNotesPage(
    IReadOnlyList<CustomerNoteView> Items,
    int TotalCount,
    int Page,
    int PageSize,
    string Timezone);

/// <summary>One whitelisted audit row (BR-17): never any before/after data, metadata, IP or note text.</summary>
public sealed record CustomerActivityItem(
    long Id,
    string Action,
    string? SubjectName,
    string? ActorName,
    DateTimeOffset OccurredAt);

public sealed record CustomerActivityPage(
    IReadOnlyList<CustomerActivityItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    string Timezone);

/// <summary>The audit actions the Activity tab may show and which of them name a property (BR-17).</summary>
public static class CustomerActivityActions
{
    public const int PageSize = 20;

    public static IReadOnlyList<string> Customer { get; } =
    [
        CustomerAuditActions.Created,
        CustomerAuditActions.Updated,
        CustomerAuditActions.Archived,
        CustomerAuditActions.Reactivated,
    ];

    public static IReadOnlyList<string> Property { get; } =
    [
        CustomerAuditActions.PropertyCreated,
        CustomerAuditActions.PropertyUpdated,
        CustomerAuditActions.PropertyPrimaryChanged,
        CustomerAuditActions.PropertyArchived,
        CustomerAuditActions.PropertyReactivated,
    ];

    public static IReadOnlyList<string> Note { get; } = [CustomerAuditActions.NoteCreated];

    /// <summary>True for the actions whose label carries the property name.</summary>
    public static bool NamesProperty(string action) => Property.Contains(action);

    public static bool IsWhitelisted(string action) =>
        Customer.Contains(action) || Property.Contains(action) || Note.Contains(action);
}

/// <summary>Wire text of the enums shown by recent work and the last invoice (snake_case).</summary>
public static class CustomerEnumText
{
    public static string Snake<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    /// <summary>The first non-blank line of a text, or null.</summary>
    public static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var line = text.Split('\n')[0].Trim();

        return line.Length == 0 ? null : line;
    }
}
