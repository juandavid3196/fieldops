using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.DemoMode;
using FieldOps.Application.Features.Messaging;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Messaging;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// technician-messages persistence. Every method resolves the caller first and composes from <see cref="MessagingAccess"/>,
/// so a foreign, out-of-scope or unknown id is the identical not found with no write. Bodies and search text are never logged.
/// </summary>
internal sealed partial class MessagingStore(
    FieldOpsDbContext dbContext,
    IBranchScopeResolver scopes,
    TimeProvider timeProvider,
    IDemoProtection demo) : IMessagingStore
{
    private const string UniqueViolation = "23505";

    private Task<(MessagingCaller? Caller, MessagingStatus Status)> CallerAsync(MessagingActor actor, CancellationToken cancellationToken) =>
        MessagingAccess.ResolveAsync(dbContext, scopes, actor, cancellationToken);

    public async Task<MessagingStoreResult<MessagingSummary>> GetSummaryAsync(MessagingActor actor, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<MessagingSummary>.Failure(status);
        }

        var unread =
            from message in MessagingAccess.Unread(dbContext, caller.OrganizationId, caller.UserId)
            join conversation in MessagingAccess.Conversations(dbContext, caller) on message.ConversationId equals conversation.Id
            select message.ConversationId;

        return MessagingStoreResult<MessagingSummary>.Ok(
            new MessagingSummary(
                await unread.CountAsync(cancellationToken),
                await unread.Distinct().CountAsync(cancellationToken)));
    }

    public async Task<MessagingStoreResult<ConversationPage>> ListAsync(
        MessagingActor actor, ConversationQuery query, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<ConversationPage>.Failure(status);
        }

        var organizationId = caller.OrganizationId;
        var userId = caller.UserId;
        var prefix = await OrganizationPrefixAsync(organizationId, cancellationToken);

        // Authorization is the source of every row; filter, search and the cursor only narrow it (BR-05).
        var orders = MessagingAccess.WorkOrders(dbContext, caller);
        var branches = MessagingAccess.Branches(dbContext, caller);
        var unread = MessagingAccess.Unread(dbContext, organizationId, userId);
        var bodies = dbContext.ConversationMessages.AsNoTracking().Where(message => message.OrganizationId == organizationId);
        var pattern = query.Search is null ? null : "%" + EscapeLike(query.Search) + "%";
        var unreadOnly = query.Filter == MessagingFilters.Unread;

        IQueryable<ListRow>? jobs = null;
        IQueryable<ListRow>? teams = null;

        if (query.Filter != MessagingFilters.Team)
        {
            jobs =
                from conversation in dbContext.Conversations.AsNoTracking()
                join order in orders on conversation.WorkOrderId equals (Guid?)order.Id
                join branch in dbContext.Branches.AsNoTracking() on order.BranchId equals branch.Id
                where conversation.OrganizationId == organizationId
                    && conversation.Type == ConversationTypes.Job
                    && conversation.LastMessageAt != null
                select new ListRow
                {
                    ConversationId = conversation.Id,
                    Type = ConversationTypes.Job,
                    BranchId = order.BranchId,
                    BranchName = branch.Name,
                    WorkOrderId = order.Id,
                    DisplayNumber = prefix + "-" + order.WorkOrderNumber.ToString(),
                    Title = order.Title,
                    LastMessageAt = conversation.LastMessageAt,
                    SortTitle = order.Title.ToLower(),
                    Key = conversation.Id,
                };
        }

        if (query.Filter is MessagingFilters.All or MessagingFilters.Unread or MessagingFilters.Team)
        {
            var teamConversations = dbContext.Conversations.AsNoTracking()
                .Where(conversation => conversation.OrganizationId == organizationId && conversation.Type == ConversationTypes.Team);

            teams =
                from branch in branches
                join conversation in teamConversations on branch.Id equals conversation.BranchId into joined
                from conversation in joined.DefaultIfEmpty()
                select new ListRow
                {
                    ConversationId = (Guid?)conversation.Id,
                    Type = ConversationTypes.Team,
                    BranchId = branch.Id,
                    BranchName = branch.Name,
                    WorkOrderId = null,
                    DisplayNumber = null,
                    Title = branch.Name + " team",
                    LastMessageAt = conversation.LastMessageAt,
                    SortTitle = (branch.Name + " team").ToLower(),
                    Key = branch.Id,
                };
        }

        IQueryable<ListRow> Narrow(IQueryable<ListRow> source)
        {
            if (unreadOnly)
            {
                source = source.Where(row => row.ConversationId != null
                    && unread.Any(message => message.ConversationId == row.ConversationId));
            }

            if (pattern is not null)
            {
                source = source.Where(row =>
                    EF.Functions.ILike(row.Title, pattern)
                    || EF.Functions.ILike(row.BranchName, pattern)
                    || (row.DisplayNumber != null && EF.Functions.ILike(row.DisplayNumber, pattern))
                    || (row.ConversationId != null
                        && bodies.Any(message => message.ConversationId == row.ConversationId && EF.Functions.ILike(message.Body, pattern))));
            }

            return source;
        }

        var sources = new[] { jobs, teams }.Where(source => source is not null).Select(source => Narrow(source!)).ToList();

        if (sources.Count == 0)
        {
            return MessagingStoreResult<ConversationPage>.Ok(new ConversationPage([], null));
        }

        var all = sources.Count == 1 ? sources[0] : sources[0].Concat(sources[1]);

        if (query.After is { } after)
        {
            var sortTitle = after.SortTitle;
            var key = after.Key;

            all = after.LastMessageAt is { } at
                ? all.Where(row => row.LastMessageAt == null
                    || row.LastMessageAt < at
                    || (row.LastMessageAt == at
                        && (EF.Functions.Collate(row.SortTitle, "C").CompareTo(sortTitle) > 0
                            || (row.SortTitle == sortTitle && row.Key.CompareTo(key) > 0))))
                : all.Where(row => row.LastMessageAt == null
                    && (EF.Functions.Collate(row.SortTitle, "C").CompareTo(sortTitle) > 0
                        || (row.SortTitle == sortTitle && row.Key.CompareTo(key) > 0)));
        }

        // BR-04: last message descending with nulls last, then the title (ordinal), then the row key.
        var rows = await all
            .OrderBy(row => row.LastMessageAt == null)
            .ThenByDescending(row => row.LastMessageAt)
            .ThenBy(row => EF.Functions.Collate(row.SortTitle, "C"))
            .ThenBy(row => row.Key)
            .Take(MessagingRules.PageSize + 1)
            .ToListAsync(cancellationToken);

        ConversationListCursor? next = null;

        if (rows.Count > MessagingRules.PageSize)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            next = new ConversationListCursor(last.LastMessageAt, last.SortTitle, last.Key);
        }

        return MessagingStoreResult<ConversationPage>.Ok(new ConversationPage(await SummariesAsync(caller, rows, cancellationToken), next));
    }

    public async Task<MessagingStoreResult<Guid>> ResolveAsync(
        MessagingActor actor, ResolveTarget target, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<Guid>.Failure(status);
        }

        var organizationId = caller.OrganizationId;
        var isJob = target.Type == ConversationTypes.Job;
        Guid branchId;

        // Participation of the conversation it would be, decided before any write (BR-11).
        if (isJob)
        {
            var order = await MessagingAccess.WorkOrders(dbContext, caller)
                .Where(candidate => candidate.Id == target.TargetId)
                .Select(candidate => new { candidate.BranchId })
                .SingleOrDefaultAsync(cancellationToken);

            if (order is null)
            {
                return MessagingStoreResult<Guid>.Failure(MessagingStatus.NotFound);
            }

            branchId = order.BranchId;
        }
        else
        {
            if (!await MessagingAccess.Branches(dbContext, caller).AnyAsync(branch => branch.Id == target.TargetId, cancellationToken))
            {
                return MessagingStoreResult<Guid>.Failure(MessagingStatus.NotFound);
            }

            branchId = target.TargetId;
        }

        async Task<Guid?> FindAsync() =>
            await dbContext.Conversations.AsNoTracking()
                .Where(conversation => conversation.OrganizationId == organizationId
                    && (isJob
                        ? conversation.Type == ConversationTypes.Job && conversation.WorkOrderId == target.TargetId
                        : conversation.Type == ConversationTypes.Team && conversation.BranchId == target.TargetId))
                .Select(conversation => (Guid?)conversation.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (await FindAsync() is { } existing)
        {
            return MessagingStoreResult<Guid>.Ok(existing);
        }

        // Read-only demo: opening a conversation that does not exist yet would create it.
        if (await demo.IsProtectedOrganizationAsync(organizationId, cancellationToken))
        {
            return MessagingStoreResult<Guid>.Failure(MessagingStatus.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var created = isJob
            ? Conversation.CreateJob(organizationId, branchId, target.TargetId, now)
            : Conversation.CreateTeam(organizationId, branchId, now);

        try
        {
            dbContext.Conversations.Add(created);
            await dbContext.SaveChangesAsync(cancellationToken);

            return MessagingStoreResult<Guid>.Ok(created.Id);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: UniqueViolation,
            ConstraintName: ConversationConfiguration.JobIndex or ConversationConfiguration.TeamIndex,
        })
        {
            // A concurrent resolve won the unique key: return its row.
            dbContext.ChangeTracker.Clear();

            return await FindAsync() is { } winner
                ? MessagingStoreResult<Guid>.Ok(winner)
                : MessagingStoreResult<Guid>.Failure(MessagingStatus.NotFound);
        }
    }

    public async Task<MessagingStoreResult<ConversationDetail>> GetDetailAsync(
        MessagingActor actor, Guid conversationId, CancellationToken cancellationToken)
    {
        var (caller, status) = await CallerAsync(actor, cancellationToken);

        if (caller is null)
        {
            return MessagingStoreResult<ConversationDetail>.Failure(status);
        }

        var conversation = await MessagingAccess.Conversations(dbContext, caller)
            .Where(candidate => candidate.Id == conversationId)
            .Select(candidate => new { candidate.Id, candidate.Type, candidate.BranchId, candidate.WorkOrderId })
            .SingleOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            return MessagingStoreResult<ConversationDetail>.Failure(MessagingStatus.NotFound);
        }

        if (conversation.Type == ConversationTypes.Team)
        {
            var teamBranch = await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == caller.OrganizationId && branch.Id == conversation.BranchId)
                .Select(branch => branch.Name)
                .SingleAsync(cancellationToken);

            return MessagingStoreResult<ConversationDetail>.Ok(
                new ConversationDetail(conversation.Id, conversation.Type, teamBranch + " team", teamBranch, null));
        }

        // The Job scope and the branch name use the work order's current branch.
        var order = await (
            from candidate in dbContext.WorkOrders.AsNoTracking()
            join branch in dbContext.Branches.AsNoTracking() on candidate.BranchId equals branch.Id
            where candidate.OrganizationId == caller.OrganizationId && candidate.Id == conversation.WorkOrderId
            select new { candidate.Id, candidate.WorkOrderNumber, candidate.Title, BranchName = branch.Name })
            .SingleAsync(cancellationToken);
        var prefix = await OrganizationPrefixAsync(caller.OrganizationId, cancellationToken);

        return MessagingStoreResult<ConversationDetail>.Ok(
            new ConversationDetail(
                conversation.Id,
                conversation.Type,
                order.Title,
                order.BranchName,
                new ConversationDetailWorkOrder(
                    order.Id,
                    RequestCardRules.DisplayNumber(prefix, order.WorkOrderNumber),
                    order.Title,
                    await VisitIdAsync(caller, order.Id, cancellationToken))));
    }

    /// <summary>
    /// BR-08: technicians only. Among the caller's assigned, openable, non-cancelled visits of the work order, the active one
    /// (the shared BR-05 definition), else the earliest upcoming, else the latest.
    /// </summary>
    private async Task<Guid?> VisitIdAsync(MessagingCaller caller, Guid workOrderId, CancellationToken cancellationToken)
    {
        if (caller.TechnicianId is not { } technicianId)
        {
            return null;
        }

        var organizationId = caller.OrganizationId;
        var openable = await dbContext.Visits.AsNoTracking()
            .Where(visit => visit.OrganizationId == organizationId
                && visit.WorkOrderId == workOrderId
                && visit.Status != VisitStatus.Unscheduled
                && visit.Status != VisitStatus.Cancelled
                && visit.ScheduledStart != null
                && visit.ScheduledEnd != null
                && dbContext.VisitAssignments.Any(assignment =>
                    assignment.VisitId == visit.Id && assignment.TechnicianId == technicianId && assignment.UnassignedAt == null))
            .Select(visit => new { visit.Id, Start = visit.ScheduledStart!.Value, visit.VisitNumber })
            .ToListAsync(cancellationToken);

        if (openable.Count == 0)
        {
            return null;
        }

        var activeIds = await ActiveVisitQuery.For(dbContext, organizationId, technicianId)
            .Where(row => row.WorkOrderId == workOrderId)
            .Select(row => row.VisitId)
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();

        return openable.Where(visit => activeIds.Contains(visit.Id)).OrderBy(visit => visit.Start).ThenBy(visit => visit.VisitNumber)
                .Select(visit => (Guid?)visit.Id).FirstOrDefault()
            ?? openable.Where(visit => visit.Start >= now).OrderBy(visit => visit.Start).ThenBy(visit => visit.VisitNumber)
                .Select(visit => (Guid?)visit.Id).FirstOrDefault()
            ?? openable.OrderByDescending(visit => visit.Start).ThenByDescending(visit => visit.VisitNumber).First().Id;
    }

    private async Task<IReadOnlyList<ConversationSummary>> SummariesAsync(
        MessagingCaller caller, IReadOnlyList<ListRow> rows, CancellationToken cancellationToken)
    {
        var organizationId = caller.OrganizationId;
        var ids = rows.Where(row => row.ConversationId != null).Select(row => row.ConversationId!.Value).ToList();
        var unreadCounts = new Dictionary<Guid, int>();
        var lastMessages = new Dictionary<Guid, ConversationMessage>();

        if (ids.Count > 0)
        {
            unreadCounts = await MessagingAccess.Unread(dbContext, organizationId, caller.UserId)
                .Where(message => ids.Contains(message.ConversationId))
                .GroupBy(message => message.ConversationId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Key, entry => entry.Count, cancellationToken);

            var latest = await (
                from conversation in dbContext.Conversations.AsNoTracking()
                join message in dbContext.ConversationMessages.AsNoTracking() on conversation.Id equals message.ConversationId
                where conversation.OrganizationId == organizationId
                    && ids.Contains(conversation.Id)
                    && message.CreatedAt == conversation.LastMessageAt
                select message)
                .ToListAsync(cancellationToken);

            lastMessages = latest.GroupBy(message => message.ConversationId)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(message => message.Id).First());
        }

        var authors = await AuthorsAsync(organizationId, lastMessages.Values.Select(message => message.AuthorUserId), cancellationToken);

        return
        [
            .. rows.Select(row =>
            {
                LastMessageView? last = null;

                if (row.ConversationId is { } id && lastMessages.TryGetValue(id, out var message))
                {
                    var author = authors[message.AuthorUserId];
                    last = new LastMessageView(
                        MessagingRules.Preview(message.Body),
                        author.Name,
                        author.Initials,
                        message.AuthorUserId == caller.UserId,
                        message.CreatedAt);
                }

                return new ConversationSummary(
                    row.ConversationId,
                    row.Type,
                    row.Title,
                    row.BranchId,
                    row.WorkOrderId is { } workOrderId ? new ConversationWorkOrderRef(workOrderId, row.DisplayNumber!) : null,
                    last,
                    row.ConversationId is { } countId && unreadCounts.TryGetValue(countId, out var count) ? count : 0);
            }),
        ];
    }

    /// <summary>
    /// BR-08 authors from their current profile and membership: technicians by their profile name, others by the user name;
    /// a user without a current membership is "Former member". Role labels reuse the role display names (AS-02).
    /// </summary>
    private async Task<Dictionary<Guid, MessageAuthor>> AuthorsAsync(
        Guid organizationId, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        var users = await dbContext.Users.AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, user.FirstName, user.LastName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);
        var members = await (
            from member in dbContext.OrganizationUsers.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on member.RoleId equals role.Id
            where member.OrganizationId == organizationId && ids.Contains(member.UserId)
            select new { member.Id, member.UserId, member.Status, role.Code, role.Name })
            .ToDictionaryAsync(member => member.UserId, cancellationToken);
        var membershipIds = members.Values.Select(member => (Guid?)member.Id).ToList();
        var profiles = await dbContext.TechnicianProfiles.AsNoTracking()
            .Where(profile => profile.OrganizationId == organizationId && membershipIds.Contains(profile.OrganizationUserId))
            .Select(profile => new { profile.OrganizationUserId, profile.FirstName, profile.LastName })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, MessageAuthor>();

        foreach (var id in ids)
        {
            if (!members.TryGetValue(id, out var member) || member.Status == UserStatus.Disabled || !users.TryGetValue(id, out var user))
            {
                result[id] = new MessageAuthor("Former member", "FM", "Former member");
                continue;
            }

            var profile = member.Code == "technician"
                ? profiles.FirstOrDefault(candidate => candidate.OrganizationUserId == member.Id)
                : null;
            var name = string.Join(
                ' ',
                new[] { profile?.FirstName ?? user.FirstName, profile?.LastName ?? user.LastName }
                    .Where(part => !string.IsNullOrWhiteSpace(part)))
                .Trim();

            result[id] = new MessageAuthor(name, RequestCardRules.Initials(name), member.Name);
        }

        return result;
    }

    private async Task<string> OrganizationPrefixAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.WorkOrderPrefix)
            .SingleAsync(cancellationToken);

    // Escapes the wildcards of ILIKE so the search is a plain contains.
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class ListRow
    {
        public Guid? ConversationId { get; init; }

        public string Type { get; init; } = string.Empty;

        public Guid BranchId { get; init; }

        public string BranchName { get; init; } = string.Empty;

        public Guid? WorkOrderId { get; init; }

        public string? DisplayNumber { get; init; }

        public string Title { get; init; } = string.Empty;

        public DateTimeOffset? LastMessageAt { get; init; }

        public string SortTitle { get; init; } = string.Empty;

        public Guid Key { get; init; }
    }
}
