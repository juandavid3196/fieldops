using System.Net;
using FieldOps.Application.Features.Access;

namespace FieldOps.Application.Features.ServiceRequests;

public sealed record RequestOrganizationContext(string Name, string? Phone, string Timezone, string RequestPrefix);

/// <summary>The authenticated caller of a mutation: session organization, user, branch scope and address.</summary>
public sealed record RequestActor(Guid OrganizationId, Guid UserId, BranchScope Scope, IPAddress? IpAddress);

public sealed record CardAvatar(string Role, string Name, string Initials);

public sealed record RequestCard(
    Guid Id,
    string Number,
    string Title,
    string? CustomerName,
    string? CategoryName,
    string DateKind,
    DateTimeOffset? Date,
    string Urgency,
    CardAvatar? Avatar,
    DateTimeOffset CreatedAt,
    bool AwaitingResponse);

public sealed record PipelineColumn(string Status, int Total, IReadOnlyList<RequestCard> Items);

public sealed record PipelineView(IReadOnlyList<PipelineColumn> Columns, string Timezone);

/// <summary>Parsed pipeline filters (BR-06). A null assignee with <see cref="Unassigned"/> false means all.</summary>
public sealed record PipelineFilter(
    Guid? AssigneeUserId,
    bool Unassigned,
    Guid? CategoryId,
    string? Urgency,
    string? Source,
    string? Created,
    string? Search);

/// <summary>One column page (FR-02); a null <see cref="Status"/> loads the first page of every column.</summary>
public sealed record PipelinePage(string? Status, int Offset);

public sealed record MetricValue(int Value, int? DeltaPercent);

public sealed record ConversionMetric(int Value, int? DeltaPoints);

public sealed record RequestMetrics(
    MetricValue NewToday,
    MetricValue AwaitingResponse,
    MetricValue AssessmentsToday,
    ConversionMetric ConversionRate);

public sealed record OptionService(Guid Id, string Name);

public sealed record OptionCategory(Guid Id, string Name, IReadOnlyList<OptionService> Services);

/// <summary><see cref="BranchIds"/> serializes as the string <c>all</c> or an array of branch ids.</summary>
public sealed record OptionAssignee(Guid UserId, string Name, string Initials, object BranchIds);

public sealed record OptionBranch(Guid Id, string Name);

public sealed record OptionTechnician(Guid Id, string Name, string Initials, Guid BranchId);

public sealed record RequestOptions(
    string RequestPrefix,
    string Timezone,
    IReadOnlyList<OptionCategory> Categories,
    IReadOnlyList<OptionAssignee> Assignees,
    IReadOnlyList<OptionBranch> Branches,
    IReadOnlyList<OptionTechnician> Technicians);

public sealed record CustomerOptionContact(Guid Id, string Name, string? Email, string? Phone, bool IsPrimary);

public sealed record CustomerOptionProperty(Guid Id, string Name, string AddressLine1, string City, bool IsPrimary);

public sealed record CustomerOption(
    Guid Id,
    string DisplayName,
    Guid BranchId,
    IReadOnlyList<CustomerOptionContact> Contacts,
    IReadOnlyList<CustomerOptionProperty> Properties);

public sealed record CustomerOptions(IReadOnlyList<CustomerOption> Items);

public sealed record NamedRef(Guid Id, string Name);

public sealed record DetailPerson(Guid UserId, string Name, string Initials);

public sealed record DetailCustomer(Guid Id, string Name);

public sealed record DetailContact(string? Name, string? Phone, string? Email);

public sealed record DetailServiceAddress(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? CountryCode,
    string? PropertyType);

public sealed record DetailAvailability(string? DateMode, string? PreferredDate, string? TimeWindow, string? SchedulingNotes);

public sealed record DetailTechnician(Guid Id, string Name, string Initials);

public sealed record DetailAssessment(
    Guid Id,
    DateTimeOffset Start,
    DateTimeOffset End,
    DetailTechnician? Technician,
    string? Purpose,
    string? InternalInstructions);

public sealed record DetailAttachment(Guid Id, string FileName, string MimeType, long SizeBytes, DateTimeOffset CreatedAt);

public sealed record DetailNote(Guid Id, string Body, string? AuthorName, DateTimeOffset CreatedAt);

public sealed record DetailActivity(string Kind, string Label, string? Detail, string ActorName, DateTimeOffset OccurredAt);

public sealed record RequestDetail(
    Guid Id,
    string Number,
    string Title,
    string Status,
    string Urgency,
    string Source,
    DateTimeOffset CreatedAt,
    NamedRef? Branch,
    DetailPerson? Assignee,
    DetailCustomer? Customer,
    DetailContact Contact,
    DetailServiceAddress? ServiceAddress,
    NamedRef? Category,
    NamedRef? Service,
    DetailAvailability? Availability,
    bool HasActiveDamage,
    string Description,
    bool AwaitingResponse,
    DetailAssessment? Assessment,
    IReadOnlyList<DetailAttachment> Attachments,
    IReadOnlyList<DetailNote> Notes,
    IReadOnlyList<DetailActivity> Activity);

public sealed record AttachmentDownload(string FileName, string MimeType, byte[] Content);

public sealed record InternalRequestInput(
    Guid CustomerId,
    Guid ContactId,
    Guid PropertyId,
    Guid CategoryId,
    Guid? ServiceId,
    string Description,
    string Urgency,
    bool HasActiveDamage,
    string AvailabilityPreferencesJson,
    DateTimeOffset? PreferredStart,
    DateTimeOffset? PreferredEnd);

public sealed record AttachmentInput(string FileName, string MimeType, byte[] Content);

/// <summary>A mutation of one request, dispatched by the store inside its locked transaction.</summary>
public abstract record RequestMutation
{
    private RequestMutation()
    {
    }

    public sealed record StartReview : RequestMutation;

    public sealed record Assign(Guid? AssigneeUserId) : RequestMutation;

    public sealed record ChangePriority(string Urgency) : RequestMutation;

    public sealed record SetBranch(Guid BranchId) : RequestMutation;

    public sealed record AddNote(string Body) : RequestMutation;

    public sealed record RequestInformation(string Body) : RequestMutation;

    public sealed record LogResponse(string Body) : RequestMutation;

    public sealed record ScheduleAssessment(
        DateTimeOffset Start,
        DateTimeOffset End,
        Guid TechnicianId,
        Guid? BranchId,
        string Purpose,
        string? InternalNotes,
        bool NotifyCustomer) : RequestMutation;

    public sealed record RescheduleAssessment(
        DateTimeOffset Start,
        DateTimeOffset End,
        Guid TechnicianId,
        string Purpose,
        string? InternalNotes,
        bool NotifyCustomer) : RequestMutation;

    public sealed record CancelAssessment(bool NotifyCustomer) : RequestMutation;

    public sealed record CompleteAssessment : RequestMutation;

    public sealed record MarkReadyForQuote : RequestMutation;

    public sealed record MoveToReview : RequestMutation;

    public sealed record CancelRequest(string Reason) : RequestMutation;

    public sealed record AddAttachments(IReadOnlyList<AttachmentInput> Files) : RequestMutation;
}

/// <summary>What the store returns after committing an information request: the data of the email to send.</summary>
public sealed record InformationRequestEmail(
    Guid RequestId,
    string RequestNumber,
    string RecipientEmail,
    string? ContactFirstName,
    string OrganizationName,
    string? OrganizationPhone,
    string MessageBody);

public enum AssessmentEmailKind
{
    Scheduled,
    Rescheduled,
    Cancelled,
}

/// <summary>The data of the assessment email sent after the commit (schedule-assessment BR-13, BR-14).</summary>
public sealed record AssessmentEmail(
    Guid RequestId,
    string RequestNumber,
    AssessmentEmailKind Kind,
    string RecipientEmail,
    string? ContactFirstName,
    string OrganizationName,
    string? OrganizationPhone,
    string TimezoneId,
    DateTimeOffset Start,
    string? TechnicianName);

public abstract record RequestMutationOutcome
{
    private RequestMutationOutcome()
    {
    }

    public sealed record Succeeded(
        RequestDetail Detail, InformationRequestEmail? Email = null, AssessmentEmail? AssessmentEmail = null) : RequestMutationOutcome;

    public sealed record NotFound : RequestMutationOutcome;

    /// <summary>
    /// An invalid transition, a concurrent change or a technician conflict; <see cref="Message"/> overrides the
    /// default title and <see cref="Code"/> is the machine-readable 409 code.
    /// </summary>
    public sealed record Conflict(
        string? Message = null, string Code = ServiceRequestMessages.RequestChangedCode) : RequestMutationOutcome;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : RequestMutationOutcome;
}

public abstract record RequestCreationOutcome
{
    private RequestCreationOutcome()
    {
    }

    public sealed record Created(RequestDetail Detail) : RequestCreationOutcome;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : RequestCreationOutcome;
}

public enum ServiceRequestResultKind
{
    Succeeded,
    Invalid,
    NotFound,
    Conflict,
}

public sealed record ServiceRequestResult<T>(
    ServiceRequestResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null,
    string? Code = null)
{
    public static ServiceRequestResult<T> Ok(T value) => new(ServiceRequestResultKind.Succeeded, value);

    public static ServiceRequestResult<T> NotFound() => new(ServiceRequestResultKind.NotFound);

    public static ServiceRequestResult<T> Conflict(
        string? message = null, string code = ServiceRequestMessages.RequestChangedCode) =>
        new(ServiceRequestResultKind.Conflict, Message: message, Code: code);

    public static ServiceRequestResult<T> Invalid(string key, string message) =>
        new(ServiceRequestResultKind.Invalid, Errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [key] = [message],
        });

    public static ServiceRequestResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(ServiceRequestResultKind.Invalid, Errors: errors);
}
