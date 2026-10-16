using System.Linq.Expressions;
using System.Text.RegularExpressions;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace FieldOps.Infrastructure.Persistence;

internal sealed partial class AuditLogStore
{
    /// <summary>The entity types whose reference BR-06 resolves.</summary>
    private static readonly string[] ReferenceTypes =
    [
        "service_request", "quote", "work_order", "visit", "customer", "invoice", "payment", "catalog_item",
        "technician_profile", "organization_user", "user_invitation", "branch", "customer_contact",
    ];

    [GeneratedRegex(@"^AUD-(\d{1,18})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventIdPattern();

    /// <summary>The visible set with every BR-03 filter and the BR-04 search applied.</summary>
    private async Task<IQueryable<AuditLog>> FilteredAsync(
        AuditViewer viewer, AuditQuery query, CancellationToken cancellationToken)
    {
        var from = query.FromUtc;
        var to = query.ToUtc;
        var filtered = AuditLogVisibility.Apply(dbContext.AuditLogs.AsNoTracking(), viewer)
            .Where(audit => audit.OccurredAt >= from && audit.OccurredAt < to);

        if (query.ActorSystem)
        {
            filtered = filtered.Where(audit => audit.ActorUserId == null);
        }
        else if (query.ActorId is { } actorId)
        {
            filtered = filtered.Where(audit => audit.ActorUserId == actorId);
        }

        if (query.Action is { } action)
        {
            filtered = filtered.Where(audit => audit.Action == action);
        }

        if (query.EntityType is { } entityType)
        {
            filtered = filtered.Where(audit => audit.EntityType == entityType);
        }

        if (query.EntityId is { } entityId)
        {
            filtered = filtered.Where(audit => audit.EntityId == entityId);
        }

        if (query.BranchId is { } branchId)
        {
            filtered = filtered.Where(audit => audit.BranchId == branchId);
        }

        if (query.Impact is { } impact)
        {
            var high = AuditCatalog.HighCodes;
            var medium = AuditCatalog.MediumCodes;

            filtered = impact switch
            {
                AuditImpact.High => filtered.Where(audit => high.Contains(audit.Action)),
                AuditImpact.Medium => filtered.Where(audit => medium.Contains(audit.Action)),
                _ => filtered.Where(audit => !high.Contains(audit.Action) && !medium.Contains(audit.Action)),
            };
        }

        return query.Search is { } term
            ? filtered.Where(await SearchAsync(viewer, filtered, term, cancellationToken))
            : filtered;
    }

    /// <summary>
    /// BR-04 as an OR of SQL conditions: the event id, the actor name, the action and entity labels, the IP address text and the
    /// resolved reference. Emails, phones and the before/after/metadata values are never matched.
    /// </summary>
    private async Task<Expression<Func<AuditLog, bool>>> SearchAsync(
        AuditViewer viewer, IQueryable<AuditLog> candidates, string term, CancellationToken cancellationToken)
    {
        var pattern = "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var parts = new List<Expression<Func<AuditLog, bool>>>();

        if (EventIdPattern().Match(term) is { Success: true } match && long.TryParse(match.Groups[1].Value, out var eventId))
        {
            parts.Add(audit => audit.Id == eventId);
        }

        var actors = dbContext.Users.AsNoTracking()
            .Where(user => EF.Functions.ILike(user.FirstName + " " + user.LastName, pattern))
            .Select(user => user.Id);
        parts.Add(audit => audit.ActorUserId != null && actors.Contains(audit.ActorUserId.Value));

        var publicLink = AuditSourceResolver.PublicLinkActions;

        if (AuditSourceResolver.Customer.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(audit => audit.ActorUserId == null && publicLink.Contains(audit.Action));
        }

        if (AuditSourceResolver.System.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(audit => audit.ActorUserId == null && !publicLink.Contains(audit.Action));
        }

        // The action and entity labels live in code, so the distinct codes of the candidates are matched in memory.
        var seen = await candidates.Select(audit => new { audit.Action, audit.EntityType }).Distinct().ToListAsync(cancellationToken);
        var actionCodes = seen.Select(row => row.Action).Distinct(StringComparer.Ordinal)
            .Where(code => AuditCatalog.Label(code).Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entityTypes = seen.Select(row => row.EntityType).Distinct(StringComparer.Ordinal)
            .Where(type => AuditCatalog.EntityLabel(type).Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (actionCodes.Length > 0)
        {
            parts.Add(audit => actionCodes.Contains(audit.Action));
        }

        if (entityTypes.Length > 0)
        {
            parts.Add(audit => entityTypes.Contains(audit.EntityType));
        }

        parts.Add(audit => audit.IpAddress != null && EF.Functions.ILike(FieldOpsDbContext.HostText(audit.IpAddress)!, pattern));

        var facts = await LoadFactsAsync(viewer.OrganizationId, cancellationToken);
        var types = seen.Select(row => row.EntityType).Distinct(StringComparer.Ordinal).Where(type => ReferenceTypes.Contains(type));

        foreach (var type in types)
        {
            var matches = References(type, viewer.OrganizationId, facts)!
                .Where(reference => EF.Functions.ILike(reference.Text, pattern))
                .Select(reference => reference.Id);
            var entity = type;

            parts.Add(audit => audit.EntityType == entity && audit.EntityId != null && matches.Contains(audit.EntityId.Value));
        }

        return AnyOf(parts);
    }

    private static Expression<Func<AuditLog, bool>> AnyOf(IReadOnlyList<Expression<Func<AuditLog, bool>>> parts)
    {
        var parameter = parts[0].Parameters[0];
        var body = parts[0].Body;

        foreach (var part in parts.Skip(1))
        {
            body = Expression.OrElse(body, ReplacingExpressionVisitor.Replace(part.Parameters[0], parameter, part.Body));
        }

        return Expression.Lambda<Func<AuditLog, bool>>(body, parameter);
    }

    /// <summary>The organization prefixes the display numbers of BR-06 are built from.</summary>
    private async Task<OrganizationFacts> LoadFactsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new OrganizationFacts(
                organization.RequestPrefix,
                organization.QuotePrefix,
                organization.WorkOrderPrefix,
                organization.InvoicePrefix,
                organization.PaymentPrefix))
            .SingleAsync(cancellationToken);

    /// <summary>
    /// One projection per entity type, shared by the search match and the display of the resolved reference so they cannot
    /// drift. Display formats are the existing ones: <c>prefix-number</c>, names and full names (BR-06).
    /// </summary>
    private IQueryable<RefRow>? References(string entityType, Guid organizationId, OrganizationFacts facts)
    {
        var requestPrefix = facts.RequestPrefix;
        var quotePrefix = facts.QuotePrefix;
        var workOrderPrefix = facts.WorkOrderPrefix;
        var invoicePrefix = facts.InvoicePrefix;
        var paymentPrefix = facts.PaymentPrefix;

        switch (entityType)
        {
            case "service_request":
                return dbContext.ServiceRequests.AsNoTracking()
                    .Where(request => request.OrganizationId == organizationId)
                    .Select(request => new RefRow { Id = request.Id, Text = requestPrefix + "-" + request.RequestNumber.ToString() });
            case "quote":
                return dbContext.Quotes.AsNoTracking()
                    .Where(quote => quote.OrganizationId == organizationId)
                    .Select(quote => new RefRow { Id = quote.Id, Text = quotePrefix + "-" + quote.QuoteNumber.ToString() });
            case "work_order":
                return dbContext.WorkOrders.AsNoTracking()
                    .Where(order => order.OrganizationId == organizationId)
                    .Select(order => new RefRow { Id = order.Id, Text = workOrderPrefix + "-" + order.WorkOrderNumber.ToString() });
            case "visit":
                return
                    from visit in dbContext.Visits.AsNoTracking()
                    join order in dbContext.WorkOrders.AsNoTracking()
                        on new { visit.OrganizationId, Id = visit.WorkOrderId } equals new { order.OrganizationId, order.Id }
                    where visit.OrganizationId == organizationId
                    select new RefRow { Id = visit.Id, Text = workOrderPrefix + "-" + order.WorkOrderNumber.ToString() };
            case "customer":
                return dbContext.Customers.AsNoTracking()
                    .Where(customer => customer.OrganizationId == organizationId)
                    .Select(customer => new RefRow { Id = customer.Id, Text = customer.DisplayName });
            case "invoice":
                return dbContext.Invoices.AsNoTracking()
                    .Where(invoice => invoice.OrganizationId == organizationId)
                    .Select(invoice => new RefRow { Id = invoice.Id, Text = invoicePrefix + "-" + invoice.InvoiceNumber.ToString() });
            case "payment":
                return dbContext.Payments.AsNoTracking()
                    .Where(payment => payment.OrganizationId == organizationId)
                    .Select(payment => new RefRow { Id = payment.Id, Text = paymentPrefix + "-" + payment.PaymentNumber.ToString() });
            case "catalog_item":
                return dbContext.CatalogItems.AsNoTracking()
                    .Where(item => item.OrganizationId == organizationId)
                    .Select(item => new RefRow { Id = item.Id, Text = item.Name });
            case "technician_profile":
                return dbContext.TechnicianProfiles.AsNoTracking()
                    .Where(profile => profile.OrganizationId == organizationId)
                    .Select(profile => new RefRow { Id = profile.Id, Text = profile.FirstName + " " + profile.LastName });
            case "organization_user":
                return
                    from member in dbContext.OrganizationUsers.AsNoTracking()
                    join user in dbContext.Users.AsNoTracking() on member.UserId equals user.Id
                    where member.OrganizationId == organizationId
                    select new RefRow { Id = member.Id, Text = user.FirstName + " " + user.LastName };
            case "user_invitation":
                return dbContext.UserInvitations.AsNoTracking()
                    .Where(invitation => invitation.OrganizationId == organizationId)
                    .Select(invitation => new RefRow { Id = invitation.Id, Text = invitation.FirstName + " " + invitation.LastName });
            case "branch":
                return dbContext.Branches.AsNoTracking()
                    .Where(branch => branch.OrganizationId == organizationId)
                    .Select(branch => new RefRow { Id = branch.Id, Text = branch.Name });
            case "customer_contact":
                return dbContext.CustomerContacts.AsNoTracking()
                    .Where(contact => contact.OrganizationId == organizationId)
                    .Select(contact => new RefRow
                    {
                        Id = contact.Id,
                        Text = contact.LastName == null ? contact.FirstName : contact.FirstName + " " + contact.LastName,
                    });
            default:
                return null;
        }
    }

    internal sealed class RefRow
    {
        public Guid Id { get; init; }

        public string Text { get; init; } = string.Empty;
    }

    internal sealed record OrganizationFacts(
        string RequestPrefix, string QuotePrefix, string WorkOrderPrefix, string InvoicePrefix, string PaymentPrefix);
}
