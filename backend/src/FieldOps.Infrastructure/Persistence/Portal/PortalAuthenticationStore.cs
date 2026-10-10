using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence.Portal;

internal sealed class PortalAuthenticationStore(FieldOpsDbContext dbContext) : IPortalAuthenticationStore
{
    // Customer portal BR-02: the contact, its customer and its organization are active and the contact links the user.
    // BR-04: the oldest link time first, then the lowest contact id.
    public async Task<IReadOnlyList<PortalLink>> GetActiveLinksAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
            from contact in dbContext.CustomerContacts.AsNoTracking()
            where contact.PortalUserId == userId && contact.IsActive
            join customer in dbContext.Customers.AsNoTracking()
                on new { contact.OrganizationId, Id = contact.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join organization in dbContext.Organizations.AsNoTracking()
                on contact.OrganizationId equals organization.Id
            where customer.IsActive && organization.IsActive
            orderby contact.PortalLinkedAt, contact.Id
            select new PortalLink(
                userId,
                contact.Id,
                customer.Id,
                organization.Id,
                customer.DisplayName,
                organization.Name,
                dbContext.OrganizationLogos.Any(logo => logo.OrganizationId == organization.Id),
                contact.PortalLinkedAt!.Value))
            .ToListAsync(cancellationToken);

    // A password reset revokes earlier sessions (customer portal BR-06). signedInAt comes from a cookie claim truncated to
    // milliseconds and PasswordChangedAt is stored truncated to milliseconds too, so a later sign-in never compares lower.
    public async Task<PortalSessionContext?> FindActiveLinkAsync(
        Guid userId,
        Guid contactId,
        Guid customerId,
        Guid organizationId,
        DateTimeOffset signedInAt,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId
                && candidate.Status == UserStatus.Active
                && (candidate.PasswordChangedAt == null || candidate.PasswordChangedAt <= signedInAt))
            .Select(candidate => new { candidate.FirstName, candidate.LastName, candidate.Email })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        var links = await GetActiveLinksAsync(userId, cancellationToken);
        var current = links.FirstOrDefault(link =>
            link.ContactId == contactId && link.CustomerId == customerId && link.OrganizationId == organizationId);

        return current is null
            ? null
            : new PortalSessionContext(
                PortalSessionViews.Build(user.FirstName, user.LastName, user.Email, current, links),
                new PortalScope(current.OrganizationId, current.CustomerId, current.ContactId, userId));
    }

    public async Task SaveAuditAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(auditLog);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Builds the response body of a portal session from the user and its links.</summary>
internal static class PortalSessionViews
{
    public static PortalSessionView Build(string firstName, string lastName, string email, PortalLink current, IReadOnlyList<PortalLink> links) =>
        new(
            new PortalSessionUser(firstName, lastName, email),
            new PortalSessionAccount(current.ContactId, current.CustomerName, current.OrganizationName, current.HasLogo),
            links.Select(link => new PortalSessionAccountSummary(link.ContactId, link.CustomerName, link.OrganizationName)).ToList());
}
