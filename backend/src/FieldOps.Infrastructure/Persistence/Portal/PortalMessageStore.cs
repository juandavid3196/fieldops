using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using FieldOps.Application.Features.PortalDashboard;
using FieldOps.Application.Features.PortalRequests;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>Messages to the organization (customer portal BR-35) and the contact data of a portal request (BR-28).</summary>
internal sealed class PortalMessageStore(FieldOpsDbContext dbContext) : IPortalMessageStore, IPortalRequestStore
{
    public async Task<PortalMessageTarget> GetTargetAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var organizationId = scope.OrganizationId;
        var customerId = scope.CustomerId;
        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Email })
            .SingleAsync(cancellationToken);
        var recipient = organization.Email;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = await dbContext.Branches.AsNoTracking()
                .Where(branch => branch.OrganizationId == organizationId && branch.IsMain)
                .Select(branch => branch.Email)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var customerName = await dbContext.Customers.AsNoTracking()
            .Where(customer => customer.OrganizationId == organizationId && customer.Id == customerId)
            .Select(customer => customer.DisplayName)
            .SingleAsync(cancellationToken);
        var contact = await dbContext.CustomerContacts.AsNoTracking().ForPortal(scope)
            .Where(candidate => candidate.Id == scope.ContactId)
            .Select(candidate => new { candidate.FirstName, candidate.LastName, candidate.Email, candidate.Phone })
            .SingleAsync(cancellationToken);

        return new PortalMessageTarget(
            organization.Name,
            string.IsNullOrWhiteSpace(recipient) ? null : recipient.Trim(),
            customerName,
            PortalReaders.FullName(contact.FirstName, contact.LastName ?? string.Empty),
            contact.Email,
            contact.Phone);
    }

    // The audit row carries the message length only, never the text (customer portal BR-35).
    public async Task WriteMessageAuditAsync(PortalScope scope, int length, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(AuditLog.Create(
            scope.OrganizationId,
            PortalAuditActions.MessageSent,
            PortalAuditActions.ContactEntityType,
            scope.UserId,
            scope.ContactId,
            ipAddress: clientIp,
            metadata: JsonSerializer.Serialize(new { length })));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PortalContactSnapshot> GetContactAsync(PortalScope scope, CancellationToken cancellationToken)
    {
        var contact = await dbContext.CustomerContacts.AsNoTracking().ForPortal(scope)
            .Where(candidate => candidate.Id == scope.ContactId)
            .Select(candidate => new
            {
                candidate.FirstName,
                candidate.LastName,
                candidate.Email,
                candidate.Phone,
                candidate.PrefersEmail,
                candidate.PrefersSms,
            })
            .SingleAsync(cancellationToken);

        return new PortalContactSnapshot(
            contact.FirstName, contact.LastName ?? string.Empty, contact.Email, contact.Phone, contact.PrefersEmail, contact.PrefersSms);
    }

    public Task<bool> IsActivePropertyAsync(PortalScope scope, Guid propertyId, CancellationToken cancellationToken) =>
        dbContext.Properties.AsNoTracking().ForPortal(scope).AnyAsync(property => property.Id == propertyId && property.IsActive, cancellationToken);
}
