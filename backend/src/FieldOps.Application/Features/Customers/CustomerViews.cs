using System.Text.Json.Serialization;
using FieldOps.Domain.Customers;

namespace FieldOps.Application.Features.Customers;

public sealed record CustomerTagView(Guid Id, string Name);

public sealed record CustomerBranchOption(Guid Id, string Name);

/// <summary>Response of GET /customers/branch-options (FR-14).</summary>
public sealed record CustomerBranchOptions(string CountryCode, IReadOnlyList<CustomerBranchOption> Branches);

public sealed record CustomerLastService(DateTimeOffset CompletedAt, string? Summary);

public sealed record CustomerNextService(DateTimeOffset StartsAt);

/// <summary>A list row (BR-09). Dates are UTC instants; the page formats them in the organization time zone.</summary>
public sealed record CustomerListItem(
    Guid Id,
    string Type,
    string DisplayName,
    string? PrimaryEmail,
    string? PrimaryPhone,
    int PropertyCount,
    CustomerLastService? LastService,
    CustomerNextService? NextService,
    decimal Balance,
    string Lifecycle,
    string DisplayStatus,
    Guid BranchId);

public sealed record CustomerTabCounts(int All, int Leads, int Active, int Archived);

public sealed record CustomerListPage(
    IReadOnlyList<CustomerListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    CustomerTabCounts TabCounts,
    string Currency,
    string Timezone);

public sealed record CustomerMetrics(
    int TotalCustomers,
    int ActiveCustomers,
    int NewThisMonth,
    decimal OutstandingBalance,
    string Currency);

public sealed record CustomerDetailContact(
    string FirstName,
    string LastName,
    string? Title,
    string Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms);

public sealed record CustomerDetailProperty(
    string AddressLine1,
    string City,
    string? StateRegion,
    string? PostalCode);

public sealed record CustomerDetail(
    Guid Id,
    string Type,
    string? CompanyName,
    CustomerDetailContact Contact,
    CustomerDetailProperty Property,
    string? ServiceInstructions,
    string? InternalNote,
    Guid BranchId,
    IReadOnlyList<CustomerTagView> Tags,
    string Lifecycle,
    string DisplayStatus,
    bool IsActive);

/// <summary>A BR-14 match. Out-of-scope matches carry only the name, matched field and status.</summary>
public sealed record CustomerDuplicateMatch(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CustomerId,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrimaryEmail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PrimaryPhone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PropertyCount,
    string DisplayStatus,
    string MatchedField,
    bool InScope);

public sealed record CustomerDuplicateCheck(IReadOnlyList<CustomerDuplicateMatch> Matches);

public sealed record CustomerImportDuplicateWarning(int Row, string MatchedField, string ExistingDisplayName);

public sealed record CustomerImportPreview(
    int ValidRowCount,
    IReadOnlyList<CustomerRowError> RowErrors,
    IReadOnlyList<CustomerImportDuplicateWarning> DuplicateWarnings);

public sealed record CustomerFile(string FileName, string ContentType, byte[] Content);

public sealed record CustomerOrganizationContext(string Currency, string Timezone, string? CountryCode);

public sealed record CustomerMetricsData(int Total, int Active, int NewThisMonth, decimal Outstanding);

/// <summary>Customer fields after validation (BR-10, BR-11), shared by the form and the CSV import.</summary>
public sealed record CustomerValues(
    CustomerType Type,
    string DisplayName,
    string? CompanyName,
    string FirstName,
    string LastName,
    string? Title,
    string Email,
    string? Phone,
    bool PrefersEmail,
    bool PrefersSms,
    string AddressLine1,
    string City,
    string? StateRegion,
    string? PostalCode,
    string? ServiceInstructions,
    string? InternalNote);

/// <summary>A validated create request, ready to persist.</summary>
public sealed record CustomerWrite(
    CustomerValues Values,
    Guid BranchId,
    IReadOnlyList<Guid> TagIds,
    string CountryCode);

/// <summary>A validated import row, ready to persist.</summary>
public sealed record CustomerImportRow(CustomerValues Values, Guid BranchId, IReadOnlyList<string> TagNames);

/// <summary>A duplicate candidate read from the database, before scope redaction.</summary>
public sealed record CustomerDuplicateRow(
    Guid CustomerId,
    string DisplayName,
    string? PrimaryEmail,
    string? PrimaryPhone,
    int PropertyCount,
    Guid BranchId,
    string DisplayStatus,
    bool EmailMatched,
    bool PhoneMatched);

/// <summary>An existing contact that shares an email or phone with an import row.</summary>
public sealed record CustomerContactMatch(string? Email, string? Phone, string DisplayName);

public enum CustomerTab
{
    All,
    Leads,
    Active,
    Archived,
}

public enum CustomerSort
{
    LastActivity,
    NameAsc,
    NameDesc,
    Newest,
    BalanceDesc,
}

public enum CustomerBalanceFilter
{
    Any,
    None,
    HasBalance,
    Overdue,
}

/// <summary>Validated list filters (BR-06 to BR-08).</summary>
public sealed record CustomerListFilter(
    CustomerTab Tab,
    string? Search,
    CustomerType? Type,
    Guid? BranchId,
    IReadOnlyList<Guid> TagIds,
    CustomerBalanceFilter Balance,
    CustomerSort Sort,
    int Page);

public sealed record CustomerListData(IReadOnlyList<CustomerListItem> Items, CustomerTabCounts TabCounts, int TotalCount);

public enum CustomerStateOutcome
{
    NotFound,
    NoChange,
    Changed,
}

public enum CustomerResultKind
{
    Succeeded,
    NoContent,
    Invalid,
    NotFound,
    Conflict,
}

public sealed record CustomerRowError(int Row, string Column, string Message);

/// <summary>Outcome of a customer use case, mapped to HTTP by the controller.</summary>
public sealed record CustomerResult<T>(
    CustomerResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyList<CustomerRowError>? RowErrors = null,
    string? Message = null)
{
    public static CustomerResult<T> Ok(T value) => new(CustomerResultKind.Succeeded, value);

    public static CustomerResult<T> NoOp() => new(CustomerResultKind.NoContent);

    public static CustomerResult<T> NotFound() => new(CustomerResultKind.NotFound);

    public static CustomerResult<T> Invalid(string key, string message) =>
        new(
            CustomerResultKind.Invalid,
            Errors: new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    public static CustomerResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(CustomerResultKind.Invalid, Errors: errors);

    public static CustomerResult<T> InvalidRows(IReadOnlyList<CustomerRowError> rowErrors) =>
        new(CustomerResultKind.Invalid, RowErrors: rowErrors);

    public static CustomerResult<T> Conflict(string message) =>
        new(CustomerResultKind.Conflict, Message: message);
}

public readonly record struct CustomerNoValue;

public static class CustomerMessages
{
    public const string TypeRequired = "Choose a customer type.";

    public const string CompanyRequired = "Enter a company name.";

    public const string CompanyTooLong = "Use 180 characters or fewer.";

    public const string FirstNameRequired = "Enter a first name.";

    public const string FirstNameTooLong = "Use 100 characters or fewer.";

    public const string LastNameRequired = "Enter a last name.";

    public const string LastNameTooLong = "Use 100 characters or fewer.";

    public const string FullNameTooLong = "Use 180 characters or fewer for the full name.";

    public const string TitleTooLong = "Use 100 characters or fewer.";

    public const string EmailRequired = "Enter an email.";

    public const string EmailInvalid = "Enter a valid email.";

    public const string PhoneInvalid = "Enter a valid phone number.";

    public const string ChannelRequired = "Choose at least one communication method.";

    public const string SmsNeedsPhone = "Add a mobile phone to use SMS.";

    public const string AddressRequired = "Enter an address.";

    public const string AddressTooLong = "Use 180 characters or fewer.";

    public const string CityRequired = "Enter a city.";

    public const string CityTooLong = "Use 100 characters or fewer.";

    public const string StateInvalid = "Choose a valid state.";

    public const string StateTooLong = "Use 100 characters or fewer.";

    public const string PostalInvalid = "Enter a valid ZIP code.";

    public const string TextTooLong = "Use 2,000 characters or fewer.";

    public const string BranchRequired = "Choose a branch.";

    public const string BranchNotAllowed = "Choose a branch you have access to.";

    public const string TagsTooMany = "Choose up to 10 tags.";

    public const string TagInvalid = "Choose a valid tag.";

    public const string TagNameRequired = "Enter a tag name.";

    public const string TagNameTooLong = "Use 40 characters or fewer.";

    public const string AlreadyArchived = "This customer is already archived.";

    public const string AlreadyActive = "This customer is already active.";
}

public static class CustomerAuditActions
{
    public const string EntityType = "customer";

    public const string TagEntityType = "customer_tag";

    public const string Created = "customer.created";

    public const string Updated = "customer.updated";

    public const string Archived = "customer.archived";

    public const string Reactivated = "customer.reactivated";

    public const string Imported = "customer.imported";

    public const string TagCreated = "customer_tag.created";
}

/// <summary>Lifecycle and display-status derivation (BR-03, BR-04).</summary>
public static class CustomerStatus
{
    public const string Lead = "lead";

    public const string Active = "active";

    public const string Archived = "archived";

    public const string Overdue = "overdue";

    public static string Lifecycle(bool isActive, bool hasCompletedWork) =>
        !isActive ? Archived : hasCompletedWork ? Active : Lead;

    /// <summary>Archived, then Overdue, then Lead, then Active.</summary>
    public static string Display(bool isActive, bool hasCompletedWork, bool hasOverdueInvoice) =>
        !isActive ? Archived : hasOverdueInvoice ? Overdue : hasCompletedWork ? Active : Lead;

    public static string TypeText(CustomerType type) => type == CustomerType.Person ? "residential" : "commercial";
}
