using System.Globalization;
using System.Text.Json;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class ServiceRequestStore
{
    private const int PageSize = 50;

    private static readonly string[] ActivityAuditActions =
    [
        "service_request.assigned",
        "service_request.priority_changed",
        "service_request.branch_changed",
        "service_request.assessment_scheduled",
        "service_request.assessment_rescheduled",
        "service_request.assessment_cancelled",
        "service_request.attachments_added",
    ];

    public async Task<PipelineView> GetPipelineAsync(
        Guid organizationId,
        BranchScope scope,
        PipelineFilter filter,
        PipelinePage page,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");
        var zone = OrganizationTime.FindZone(organization.Timezone);

        var query = ApplyFilter(Visible(organizationId, scope), filter, organization.RequestPrefix, zone, now);

        var statuses = page.Status is { } code && RequestTransitions.TryParseCode(code, out var single)
            ? new[] { single }
            : RequestTransitions.Open.ToArray();

        var counts = await query.Where(IsOpenRequest)
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var pages = new List<(RequestStatus Status, int Total, List<CardRow> Rows)>();

        foreach (var status in statuses)
        {
            var current = status;
            var rows = await query
                .Where(request => request.Status == current)
                .OrderBy(request => request.Urgency == "emergency" ? 0 : request.Urgency == "urgent" ? 1 : 2)
                .ThenBy(request => request.CreatedAt)
                .ThenBy(request => request.RequestNumber)
                .Skip(page.Offset)
                .Take(PageSize)
                .Select(request => new CardRow(
                    request.Id,
                    request.RequestNumber,
                    request.Status,
                    request.CustomerId,
                    request.GuestName,
                    request.CategoryId,
                    request.CatalogItemId,
                    request.Urgency,
                    request.PreferredStart,
                    request.AvailabilityPreferences,
                    request.CreatedAt,
                    request.AssignedDispatcherUserId,
                    request.Description))
                .ToListAsync(cancellationToken);

            pages.Add((status, counts.FirstOrDefault(count => count.Status == status)?.Count ?? 0, rows));
        }

        var allRows = pages.SelectMany(entry => entry.Rows).ToList();
        var cards = await BuildCardsAsync(organizationId, organization.RequestPrefix, zone, allRows, cancellationToken);

        return new PipelineView(
            pages
                .Select(entry => new PipelineColumn(
                    RequestTransitions.Code(entry.Status),
                    entry.Total,
                    entry.Rows.Select(row => cards[row.Id]).ToList()))
                .ToList(),
            organization.Timezone);
    }

    public async Task<RequestMetrics> GetMetricsAsync(
        Guid organizationId, BranchScope scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");
        var zone = OrganizationTime.FindZone(organization.Timezone);
        var visible = Visible(organizationId, scope);

        var today = OrganizationTime.LocalDate(now, zone);
        DateTimeOffset Day(int offsetDays) => OrganizationTime.StartOfDayUtc(today.AddDays(offsetDays), zone);

        var newToday = await CountCreatedAsync(visible, Day(0), Day(1), cancellationToken);
        var newLastWeek = await CountCreatedAsync(visible, Day(-7), Day(-6), cancellationToken);

        var awaiting = await AwaitingQuery(visible, organizationId, null).CountAsync(cancellationToken);
        var awaitingBefore = await AwaitingQuery(visible, organizationId, now.AddDays(-7)).CountAsync(cancellationToken);

        var assessmentsToday = await CountAssessmentsAsync(visible, organizationId, Day(0), Day(1), cancellationToken);
        var assessmentsLastWeek = await CountAssessmentsAsync(visible, organizationId, Day(-7), Day(-6), cancellationToken);

        var rate = await ConversionRateAsync(visible, Day(-29), Day(1), cancellationToken);
        var previousRate = await ConversionRateAsync(visible, Day(-59), Day(-29), cancellationToken);

        return new RequestMetrics(
            new MetricValue(newToday, RequestMetricMath.DeltaPercent(newToday, newLastWeek)),
            new MetricValue(awaiting, RequestMetricMath.DeltaPercent(awaiting, awaitingBefore)),
            new MetricValue(assessmentsToday, RequestMetricMath.DeltaPercent(assessmentsToday, assessmentsLastWeek)),
            new ConversionMetric(rate, RequestMetricMath.DeltaPoints(rate, previousRate)));
    }

    public async Task<RequestDetail?> GetDetailAsync(
        Guid organizationId, BranchScope scope, Guid requestId, CancellationToken cancellationToken)
    {
        var request = await Visible(organizationId, scope)
            .Where(candidate => candidate.Id == requestId)
            .SingleOrDefaultAsync(cancellationToken);

        return request is null ? null : await BuildDetailAsync(request, cancellationToken);
    }

    public async Task<AttachmentDownload?> GetAttachmentAsync(
        Guid organizationId, BranchScope scope, Guid requestId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var visible = await Visible(organizationId, scope).AnyAsync(request => request.Id == requestId, cancellationToken);

        if (!visible)
        {
            return null;
        }

        // The only query that projects the binary content (BR-16).
        var row = await dbContext.RequestAttachments.AsNoTracking()
            .Where(attachment => attachment.Id == attachmentId
                && attachment.RequestId == requestId
                && attachment.OrganizationId == organizationId
                && attachment.Content != null)
            .Select(attachment => new { attachment.FileName, attachment.MimeType, attachment.Content })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new AttachmentDownload(row.FileName, row.MimeType, row.Content!);
    }

    private IQueryable<ServiceRequest> ApplyFilter(
        IQueryable<ServiceRequest> query,
        PipelineFilter filter,
        string requestPrefix,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        if (filter.Unassigned)
        {
            query = query.Where(request => request.AssignedDispatcherUserId == null);
        }
        else if (filter.AssigneeUserId is { } assigneeId)
        {
            query = query.Where(request => request.AssignedDispatcherUserId == assigneeId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(request => request.CategoryId == categoryId);
        }

        if (filter.Urgency is { } urgency)
        {
            query = query.Where(request => request.Urgency == urgency);
        }

        if (filter.Source is { } source)
        {
            query = query.Where(request => request.Source == source);
        }

        if (filter.Created is { } created)
        {
            var today = OrganizationTime.LocalDate(now, zone);
            var from = OrganizationTime.StartOfDayUtc(
                today.AddDays(created switch { "today" => 0, "7d" => -6, _ => -29 }), zone);
            query = query.Where(request => request.CreatedAt >= from);
        }

        if (filter.Search is { } term)
        {
            var pattern = RequestSearch.ContainsPattern(term);
            var escape = RequestSearch.EscapeCharacter.ToString();
            var byNumber = RequestSearch.TryParseNumber(term, requestPrefix, out var number);
            var requestNumber = byNumber ? number : -1L;

            query = query.Where(request =>
                (byNumber && request.RequestNumber == requestNumber)
                || (request.GuestName != null && EF.Functions.ILike(request.GuestName, pattern, escape))
                || (request.GuestEmail != null && EF.Functions.ILike(request.GuestEmail, pattern, escape))
                || dbContext.CatalogItems.Any(item =>
                    item.Id == request.CatalogItemId
                    && item.OrganizationId == request.OrganizationId
                    && EF.Functions.ILike(item.Name, pattern, escape))
                || dbContext.ServiceCategories.Any(category =>
                    category.Id == request.CategoryId
                    && category.OrganizationId == request.OrganizationId
                    && EF.Functions.ILike(category.Name, pattern, escape))
                || dbContext.Customers.Any(customer =>
                    customer.Id == request.CustomerId
                    && customer.OrganizationId == request.OrganizationId
                    && EF.Functions.ILike(customer.DisplayName, pattern, escape)));
        }

        return query;
    }

    /// <summary>
    /// Requests whose BR-14 holds at the instant: open and with a latest customer message that has no
    /// contact author. With an instant, status and messages are read as of that time (BR-07).
    /// </summary>
    private IQueryable<ServiceRequest> AwaitingQuery(
        IQueryable<ServiceRequest> source, Guid organizationId, DateTimeOffset? asOf)
    {
        if (asOf is not { } instant)
        {
            return source
                .Where(IsOpenRequest)
                .Where(request => dbContext.RequestMessages
                    .Where(message => message.OrganizationId == organizationId
                        && message.RequestId == request.Id
                        && message.Visibility == MessageVisibility.Customer)
                    .OrderByDescending(message => message.CreatedAt)
                    .Take(1)
                    .Any(message => message.AuthorContactId == null));
        }

        return source
            .Where(request => request.CreatedAt <= instant)
            .Where(request => dbContext.RequestStatusHistories
                .Where(history => history.OrganizationId == organizationId
                    && history.RequestId == request.Id
                    && history.ChangedAt <= instant)
                .OrderByDescending(history => history.ChangedAt)
                .Take(1)
                .Any(history => history.ToStatus == RequestStatus.New
                    || history.ToStatus == RequestStatus.NeedsReview
                    || history.ToStatus == RequestStatus.AssessmentScheduled
                    || history.ToStatus == RequestStatus.ReadyForQuote))
            .Where(request => dbContext.RequestMessages
                .Where(message => message.OrganizationId == organizationId
                    && message.RequestId == request.Id
                    && message.Visibility == MessageVisibility.Customer
                    && message.CreatedAt <= instant)
                .OrderByDescending(message => message.CreatedAt)
                .Take(1)
                .Any(message => message.AuthorContactId == null));
    }

    private static Task<int> CountCreatedAsync(
        IQueryable<ServiceRequest> visible, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        visible.CountAsync(request => request.CreatedAt >= from && request.CreatedAt < to, cancellationToken);

    private Task<int> CountAssessmentsAsync(
        IQueryable<ServiceRequest> visible,
        Guid organizationId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.Status == AssessmentStatus.Scheduled
                && assessment.ScheduledStart >= from
                && assessment.ScheduledStart < to)
            .CountAsync(
                assessment => visible.Any(request => request.Id == assessment.RequestId),
                cancellationToken);

    /// <summary>Requests created in the period that reached ready for quote, quoted or converted, over non-cancelled ones (BR-07).</summary>
    private async Task<int> ConversionRateAsync(
        IQueryable<ServiceRequest> visible, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var cohort = visible.Where(request => request.CreatedAt >= from && request.CreatedAt < to);

        var denominator = await cohort.CountAsync(request => request.Status != RequestStatus.Cancelled, cancellationToken);
        var numerator = await cohort.CountAsync(
            request => dbContext.RequestStatusHistories.Any(history =>
                history.RequestId == request.Id
                && (history.ToStatus == RequestStatus.ReadyForQuote
                    || history.ToStatus == RequestStatus.Quoted
                    || history.ToStatus == RequestStatus.Converted)),
            cancellationToken);

        return RequestMetricMath.Rate(numerator, denominator);
    }

    private async Task<Dictionary<Guid, RequestCard>> BuildCardsAsync(
        Guid organizationId,
        string requestPrefix,
        TimeZoneInfo zone,
        IReadOnlyList<CardRow> rows,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, RequestCard>(rows.Count);

        if (rows.Count == 0)
        {
            return result;
        }

        var requestIds = rows.Select(row => row.Id).ToArray();
        var categoryIds = rows.Where(row => row.CategoryId != null).Select(row => row.CategoryId!.Value).Distinct().ToArray();
        var itemIds = rows.Where(row => row.CatalogItemId != null).Select(row => row.CatalogItemId!.Value).Distinct().ToArray();
        var customerIds = rows.Where(row => row.CustomerId != null).Select(row => row.CustomerId!.Value).Distinct().ToArray();

        var categories = await dbContext.ServiceCategories.AsNoTracking()
            .Where(category => category.OrganizationId == organizationId && categoryIds.Contains(category.Id))
            .ToDictionaryAsync(category => category.Id, category => category.Name, cancellationToken);

        var items = await dbContext.CatalogItems.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        var customers = await dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId && customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id, customer => customer.DisplayName, cancellationToken);

        var assessments = await dbContext.Assessments.AsNoTracking()
            .Where(assessment => assessment.OrganizationId == organizationId
                && assessment.Status == AssessmentStatus.Scheduled
                && requestIds.Contains(assessment.RequestId))
            .Select(assessment => new { assessment.RequestId, assessment.ScheduledStart, assessment.TechnicianId })
            .ToListAsync(cancellationToken);

        var technicianIds = assessments.Where(a => a.TechnicianId != null).Select(a => a.TechnicianId!.Value).Distinct().ToArray();
        var technicians = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(technician => technician.OrganizationId == organizationId && technicianIds.Contains(technician.Id))
            .Select(technician => new { technician.Id, technician.FirstName, technician.LastName })
            .ToDictionaryAsync(technician => technician.Id, technician => FullName(technician.FirstName, technician.LastName), cancellationToken);

        var assigneeIds = rows.Where(row => row.AssigneeUserId != null).Select(row => row.AssigneeUserId!.Value).Distinct().ToArray();
        var assignees = await UserNamesAsync(assigneeIds, cancellationToken);

        var awaiting = (await AwaitingQuery(
                dbContext.ServiceRequests.AsNoTracking().Where(request => request.OrganizationId == organizationId && requestIds.Contains(request.Id)),
                organizationId,
                null)
            .Select(request => request.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var row in rows)
        {
            var assessment = assessments.FirstOrDefault(candidate => candidate.RequestId == row.Id);
            var serviceName = row.CatalogItemId is { } itemId && items.TryGetValue(itemId, out var itemName) ? itemName : null;
            var categoryName = row.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var name) ? name : null;
            var customerName = row.CustomerId is { } customerId && customers.TryGetValue(customerId, out var display)
                ? display
                : row.GuestName;

            var (kind, date) = RequestCardRules.CardDate(
                row.Status, assessment?.ScheduledStart, ReadDateMode(row.AvailabilityPreferences), row.PreferredStart, row.CreatedAt);

            CardAvatar? avatar = null;

            if (assessment?.TechnicianId is { } technicianId && technicians.TryGetValue(technicianId, out var technicianName))
            {
                avatar = new CardAvatar("technician", technicianName, RequestCardRules.Initials(technicianName));
            }
            else if (row.AssigneeUserId is { } assigneeId && assignees.TryGetValue(assigneeId, out var assigneeName))
            {
                avatar = new CardAvatar("assignee", assigneeName, RequestCardRules.Initials(assigneeName));
            }

            result[row.Id] = new RequestCard(
                row.Id,
                RequestCardRules.DisplayNumber(requestPrefix, row.RequestNumber),
                RequestCardRules.Title(serviceName, categoryName, row.Description),
                customerName,
                categoryName,
                kind,
                date is { } instant ? OrganizationTime.ToZone(instant, zone) : null,
                row.Urgency,
                avatar,
                row.CreatedAt,
                awaiting.Contains(row.Id));
        }

        return result;
    }

    private async Task<RequestDetail> BuildDetailAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId;
        var organization = await GetOrganizationContextAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException("The organization is no longer available.");
        var zone = OrganizationTime.FindZone(organization.Timezone);

        var branch = request.BranchId is { } branchId
            ? await dbContext.Branches.AsNoTracking()
                .Where(candidate => candidate.Id == branchId && candidate.OrganizationId == organizationId)
                .Select(candidate => new NamedRef(candidate.Id, candidate.Name))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var category = request.CategoryId is { } categoryId
            ? await dbContext.ServiceCategories.AsNoTracking()
                .Where(candidate => candidate.Id == categoryId && candidate.OrganizationId == organizationId)
                .Select(candidate => new NamedRef(candidate.Id, candidate.Name))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var service = request.CatalogItemId is { } itemId
            ? await dbContext.CatalogItems.AsNoTracking()
                .Where(candidate => candidate.Id == itemId && candidate.OrganizationId == organizationId)
                .Select(candidate => new NamedRef(candidate.Id, candidate.Name))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var customer = request.CustomerId is { } customerId
            ? await dbContext.Customers.AsNoTracking()
                .Where(candidate => candidate.Id == customerId && candidate.OrganizationId == organizationId)
                .Select(candidate => new DetailCustomer(candidate.Id, candidate.DisplayName))
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var contactRow = request.ContactId is { } contactId
            ? await dbContext.CustomerContacts.AsNoTracking()
                .Where(candidate => candidate.Id == contactId && candidate.OrganizationId == organizationId)
                .Select(candidate => new { candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone })
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        var contact = new DetailContact(
            contactRow is null ? request.GuestName : FullName(contactRow.FirstName, contactRow.LastName),
            FirstNonEmpty(contactRow?.Phone, request.GuestPhone),
            FirstNonEmpty(contactRow?.Email, request.GuestEmail));

        var assessmentRow = await dbContext.Assessments.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.RequestId == request.Id
                && candidate.Status == AssessmentStatus.Scheduled)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ScheduledStart,
                candidate.ScheduledEnd,
                candidate.TechnicianId,
                candidate.Purpose,
                candidate.InternalNotes,
            })
            .FirstOrDefaultAsync(cancellationToken);

        DetailTechnician? technician = null;

        if (assessmentRow?.TechnicianId is { } technicianId)
        {
            var row = await dbContext.TechnicianProfiles.AsNoTracking()
                .Where(candidate => candidate.Id == technicianId && candidate.OrganizationId == organizationId)
                .Select(candidate => new { candidate.Id, candidate.FirstName, candidate.LastName })
                .SingleOrDefaultAsync(cancellationToken);

            if (row is not null)
            {
                var name = FullName(row.FirstName, row.LastName);
                technician = new DetailTechnician(row.Id, name, RequestCardRules.Initials(name));
            }
        }

        var attachments = await dbContext.RequestAttachments.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.RequestId == request.Id)
            .OrderBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => new DetailAttachment(
                candidate.Id, candidate.FileName, candidate.MimeType, candidate.SizeBytes, candidate.CreatedAt))
            .ToListAsync(cancellationToken);

        var messages = await dbContext.RequestMessages.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.RequestId == request.Id)
            .OrderBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var history = await dbContext.RequestStatusHistories.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.RequestId == request.Id)
            .OrderBy(candidate => candidate.ChangedAt)
            .ToListAsync(cancellationToken);

        var audits = await dbContext.AuditLogs.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId
                && candidate.EntityType == AuditEntityType
                && candidate.EntityId == request.Id
                && ActivityAuditActions.Contains(candidate.Action))
            .OrderBy(candidate => candidate.OccurredAt)
            .ThenBy(candidate => candidate.Id)
            .ToListAsync(cancellationToken);

        var userIds = new HashSet<Guid>();

        if (request.AssignedDispatcherUserId is { } assigneeId)
        {
            userIds.Add(assigneeId);
        }

        userIds.UnionWith(messages.Where(m => m.AuthorUserId != null).Select(m => m.AuthorUserId!.Value));
        userIds.UnionWith(history.Where(h => h.ChangedByUserId != null).Select(h => h.ChangedByUserId!.Value));
        userIds.UnionWith(audits.Where(a => a.ActorUserId != null).Select(a => a.ActorUserId!.Value));

        var auditAssignees = audits
            .Where(a => a.Action == "service_request.assigned")
            .Select(a => ReadGuid(a.AfterData, "assigneeUserId"))
            .Where(id => id != null)
            .Select(id => id!.Value)
            .ToList();
        userIds.UnionWith(auditAssignees);

        var users = await UserNamesAsync(userIds.ToArray(), cancellationToken);

        var auditBranchIds = audits
            .Where(a => a.Action == "service_request.branch_changed")
            .Select(a => ReadGuid(a.AfterData, "branchId"))
            .Where(id => id != null)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var branchNames = await dbContext.Branches.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && auditBranchIds.Contains(candidate.Id))
            .ToDictionaryAsync(candidate => candidate.Id, candidate => candidate.Name, cancellationToken);

        string Name(Guid? userId, string fallback) =>
            userId is { } id && users.TryGetValue(id, out var name) ? name : fallback;

        var activity = new List<DetailActivity>();

        foreach (var entry in history)
        {
            if (entry.FromStatus is null)
            {
                activity.Add(new DetailActivity(
                    "submitted",
                    "Request submitted",
                    request.Source == "internal" ? $"Created by {Name(entry.ChangedByUserId, "System")}" : "Online form",
                    request.Source == "internal" ? Name(entry.ChangedByUserId, "System") : "Customer",
                    entry.ChangedAt));
            }
            else
            {
                activity.Add(new DetailActivity(
                    "status_changed",
                    $"Status changed to {RequestTransitions.Label(entry.ToStatus)}",
                    entry.Reason,
                    Name(entry.ChangedByUserId, "System"),
                    entry.ChangedAt));
            }
        }

        foreach (var audit in audits)
        {
            var actor = Name(audit.ActorUserId, "System");

            switch (audit.Action)
            {
                case "service_request.assigned":
                    var assigned = ReadGuid(audit.AfterData, "assigneeUserId");
                    activity.Add(new DetailActivity(
                        "assigned",
                        assigned is null ? "Unassigned" : $"Assigned to {Name(assigned, "a team member")}",
                        null,
                        actor,
                        audit.OccurredAt));
                    break;

                case "service_request.priority_changed":
                    activity.Add(new DetailActivity(
                        "priority_changed",
                        $"Priority changed to {PriorityLabel(ReadString(audit.AfterData, "urgency"))}",
                        null,
                        actor,
                        audit.OccurredAt));
                    break;

                case "service_request.branch_changed":
                    var changedBranch = ReadGuid(audit.AfterData, "branchId");
                    activity.Add(new DetailActivity(
                        "branch_changed",
                        $"Branch set to {(changedBranch is { } bid && branchNames.TryGetValue(bid, out var bn) ? bn : "a branch")}",
                        null,
                        actor,
                        audit.OccurredAt));
                    break;

                case "service_request.attachments_added":
                    var count = ReadInt(audit.Metadata, "count");
                    activity.Add(new DetailActivity(
                        "file_added",
                        "File added",
                        count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{count} files") : null,
                        actor,
                        audit.OccurredAt));
                    break;

                default:
                    var verb = audit.Action["service_request.assessment_".Length..];
                    var start = ReadDate(audit.Metadata, "start");
                    activity.Add(new DetailActivity(
                        $"assessment_{verb}",
                        $"Assessment {verb}",
                        AssessmentDetail(start is { } instant ? FormatLocal(instant, zone) : null, ReadString(audit.Metadata, "notified")),
                        actor,
                        audit.OccurredAt));
                    break;
            }
        }

        foreach (var message in messages.Where(m => m.Visibility == MessageVisibility.Customer))
        {
            var logged = message.AuthorContactId is not null;

            activity.Add(new DetailActivity(
                logged ? "response_logged" : "information_requested",
                logged ? "Customer response logged" : "Information requested",
                message.Body,
                Name(message.AuthorUserId, logged ? "Customer" : "System"),
                message.CreatedAt));
        }

        var notes = messages
            .Where(m => m.Visibility == MessageVisibility.Internal)
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Select(m => new DetailNote(
                m.Id, m.Body, m.AuthorUserId is { } author && users.TryGetValue(author, out var n) ? n : null, m.CreatedAt))
            .ToList();

        var latestCustomerMessage = messages.LastOrDefault(m => m.Visibility == MessageVisibility.Customer);
        var awaiting = RequestTransitions.IsOpen(request.Status)
            && latestCustomerMessage is not null
            && latestCustomerMessage.AuthorContactId is null;

        DetailPerson? assignee = null;

        if (request.AssignedDispatcherUserId is { } assignedUser && users.TryGetValue(assignedUser, out var assigneeName))
        {
            assignee = new DetailPerson(assignedUser, assigneeName, RequestCardRules.Initials(assigneeName));
        }

        return new RequestDetail(
            request.Id,
            RequestCardRules.DisplayNumber(organization.RequestPrefix, request.RequestNumber),
            RequestCardRules.Title(service?.Name, category?.Name, request.Description),
            RequestTransitions.Code(request.Status),
            request.Urgency,
            request.Source,
            request.CreatedAt,
            branch,
            assignee,
            customer,
            contact,
            ReadServiceAddress(request.ServiceAddress),
            category,
            service,
            ReadAvailability(request.AvailabilityPreferences),
            request.HasActiveDamage,
            request.Description,
            awaiting,
            assessmentRow is null
                ? null
                : new DetailAssessment(
                    assessmentRow.Id,
                    OrganizationTime.ToZone(assessmentRow.ScheduledStart, zone),
                    OrganizationTime.ToZone(assessmentRow.ScheduledEnd, zone),
                    technician,
                    assessmentRow.Purpose,
                    assessmentRow.InternalNotes),
            attachments,
            notes,
            activity.OrderBy(entry => entry.OccurredAt).ToList());
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(Guid[] userIds, CancellationToken cancellationToken)
    {
        if (userIds.Length == 0)
        {
            return [];
        }

        var rows = await dbContext.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FirstName, user.LastName })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.Id, row => FullName(row.FirstName, row.LastName));
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : string.IsNullOrWhiteSpace(second) ? null : second;

    private static string PriorityLabel(string? urgency) => urgency switch
    {
        "urgent" => "Urgent",
        "emergency" => "Emergency",
        _ => "Standard",
    };

    // BR-16: the date and, when the intent was recorded, " · Customer notified by email"; never message content.
    private static string? AssessmentDetail(string? date, string? notified) =>
        notified == "email" ? (date is null ? "Customer notified by email" : date + " · Customer notified by email") : date;

    private static string FormatLocal(DateTimeOffset instant, TimeZoneInfo zone) =>
        OrganizationTime.ToZone(instant, zone).ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    private static string? ReadDateMode(string? availabilityJson) => ReadString(availabilityJson, "dateMode");

    private static DetailAvailability? ReadAvailability(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return new DetailAvailability(
            ReadString(json, "dateMode"),
            ReadString(json, "preferredDate"),
            ReadString(json, "timeWindow"),
            ReadString(json, "schedulingNotes"));
    }

    private static DetailServiceAddress? ReadServiceAddress(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return new DetailServiceAddress(
            ReadString(json, "line1"),
            ReadString(json, "line2"),
            ReadString(json, "city"),
            ReadString(json, "state"),
            ReadString(json, "postalCode"),
            ReadString(json, "countryCode"),
            ReadString(json, "propertyType"));
    }

    private static string? ReadString(string? json, string property)
    {
        if (TryReadProperty(json, property, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        return null;
    }

    private static Guid? ReadGuid(string? json, string property) =>
        Guid.TryParse(ReadString(json, property), out var value) ? value : null;

    private static int ReadInt(string? json, string property) =>
        TryReadProperty(json, property, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : 0;

    private static DateTimeOffset? ReadDate(string? json, string property) =>
        DateTimeOffset.TryParse(ReadString(json, property), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;

    private static bool TryReadProperty(string? json, string property, out JsonElement element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(property, out var found))
            {
                element = found.Clone();

                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private sealed record CardRow(
        Guid Id,
        long RequestNumber,
        RequestStatus Status,
        Guid? CustomerId,
        string? GuestName,
        Guid? CategoryId,
        Guid? CatalogItemId,
        string Urgency,
        DateTimeOffset? PreferredStart,
        string? AvailabilityPreferences,
        DateTimeOffset CreatedAt,
        Guid? AssigneeUserId,
        string Description);
}
