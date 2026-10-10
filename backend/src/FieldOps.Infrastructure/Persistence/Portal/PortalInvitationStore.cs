using System.Net;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using FieldOps.Application.Features.PortalInvitations;
using FieldOps.Application.Features.ServiceRequests;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence.Portal;

/// <summary>
/// Persistence of portal invitations (customer portal BR-10 … BR-14). Invite and activation are single transactions; the
/// activation locks the invitation row <c>FOR UPDATE</c> and rechecks BR-12 under the lock, and the partial unique indexes
/// (one open invitation per contact, one link per user and organization) are the backstop. Nothing here logs or audits an
/// email address, a token or a password.
/// </summary>
internal sealed class PortalInvitationStore(FieldOpsDbContext dbContext, TimeProvider timeProvider) : IPortalInvitationStore
{
    public async Task<PortalStatusView?> GetStatusAsync(
        Guid organizationId, Guid customerId, string timezone, CancellationToken cancellationToken)
    {
        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.CustomerId == customerId && candidate.IsPrimary)
            .Select(candidate => new { candidate.Id, candidate.PortalUserId, candidate.PortalLinkedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (contact is null)
        {
            return null;
        }

        var zone = OrganizationTime.FindZone(timezone);

        if (contact.PortalUserId is not null && contact.PortalLinkedAt is { } linkedAt)
        {
            return new PortalStatusView(PortalStatusCodes.Active, OrganizationTime.LocalDate(linkedAt, zone), null);
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = await dbContext.CustomerPortalInvitations.AsNoTracking()
            .Where(invitation => invitation.OrganizationId == organizationId
                && invitation.ContactId == contact.Id
                && invitation.AcceptedAt == null
                && invitation.RevokedAt == null
                && invitation.ExpiresAt > now)
            .Select(invitation => (DateTimeOffset?)invitation.ExpiresAt)
            .SingleOrDefaultAsync(cancellationToken);

        return expiresAt is { } expires
            ? new PortalStatusView(PortalStatusCodes.Invited, null, OrganizationTime.LocalDate(expires, zone))
            : new PortalStatusView(PortalStatusCodes.NotInvited, null, null);
    }

    public async Task<PortalInviteResult> InviteAsync(
        Guid organizationId,
        Guid customerId,
        Guid invitedByUserId,
        string rawToken,
        string tokenHash,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await InviteOnceAsync(organizationId, customerId, invitedByUserId, rawToken, tokenHash, clientIp, cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception) && attempt == 0)
            {
                // A concurrent invite won the open-invitation index: revoke and insert again once.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private async Task<PortalInviteResult> InviteOnceAsync(
        Guid organizationId,
        Guid customerId,
        Guid invitedByUserId,
        string rawToken,
        string tokenHash,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = await dbContext.Customers.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.Id == customerId)
            .Select(candidate => new { candidate.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (customer is null)
        {
            return new PortalInviteResult.NotFound();
        }

        var contact = await dbContext.CustomerContacts.AsNoTracking()
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.CustomerId == customerId && candidate.IsPrimary)
            .Select(candidate => new { candidate.Id, candidate.FirstName, candidate.Email, candidate.IsActive, candidate.PortalUserId })
            .SingleOrDefaultAsync(cancellationToken);

        if (!customer.IsActive
            || contact is null
            || !contact.IsActive
            || string.IsNullOrWhiteSpace(contact.Email)
            || contact.PortalUserId is not null)
        {
            return new PortalInviteResult.Unavailable();
        }

        var now = timeProvider.GetUtcNow();
        var contactId = contact.Id;

        await dbContext.CustomerPortalInvitations
            .Where(invitation => invitation.ContactId == contactId && invitation.AcceptedAt == null && invitation.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(invitation => invitation.RevokedAt, now), cancellationToken);

        var invitation = CustomerPortalInvitation.Create(organizationId, contactId, contact.Email, tokenHash, invitedByUserId, now);

        dbContext.CustomerPortalInvitations.Add(invitation);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            PortalAuditActions.InvitationSent,
            PortalAuditActions.ContactEntityType,
            invitedByUserId,
            contactId,
            ipAddress: clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var organization = await dbContext.Organizations.AsNoTracking()
            .Where(candidate => candidate.Id == organizationId)
            .Select(candidate => new { candidate.Name, candidate.Timezone })
            .SingleAsync(cancellationToken);
        var expiresOn = OrganizationTime.LocalDate(invitation.ExpiresAt, OrganizationTime.FindZone(organization.Timezone));

        return new PortalInviteResult.Sent(
            new PortalStatusView(PortalStatusCodes.Invited, null, expiresOn),
            new PortalInvitationEmailData(invitation.Email, contact.FirstName, organization.Name, expiresOn, rawToken));
    }

    public async Task<PortalStatusView?> RemoveAccessAsync(
        Guid organizationId,
        Guid customerId,
        Guid actorUserId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var contact = await dbContext.CustomerContacts
            .Where(candidate => candidate.OrganizationId == organizationId && candidate.CustomerId == customerId && candidate.IsPrimary)
            .SingleOrDefaultAsync(cancellationToken);

        if (contact is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var contactId = contact.Id;
        var unlinked = contact.UnlinkPortal(now);
        var revoked = await dbContext.CustomerPortalInvitations
            .Where(invitation => invitation.ContactId == contactId && invitation.AcceptedAt == null && invitation.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(invitation => invitation.RevokedAt, now), cancellationToken);

        // Idempotent: nothing changed, nothing is written (not even an audit row).
        if (unlinked || revoked > 0)
        {
            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                PortalAuditActions.AccessRemoved,
                PortalAuditActions.ContactEntityType,
                actorUserId,
                contactId,
                ipAddress: clientIp));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new PortalStatusView(PortalStatusCodes.NotInvited, null, null);
    }

    public async Task<PortalUsableInvitation?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var row = await (
            from invitation in dbContext.CustomerPortalInvitations.AsNoTracking()
            where invitation.TokenHash == tokenHash
                && invitation.AcceptedAt == null
                && invitation.RevokedAt == null
                && invitation.ExpiresAt > now
            join contact in dbContext.CustomerContacts.AsNoTracking()
                on new { invitation.OrganizationId, Id = invitation.ContactId } equals new { contact.OrganizationId, contact.Id }
            join customer in dbContext.Customers.AsNoTracking()
                on new { contact.OrganizationId, Id = contact.CustomerId } equals new { customer.OrganizationId, customer.Id }
            join organization in dbContext.Organizations.AsNoTracking()
                on invitation.OrganizationId equals organization.Id
            where contact.IsActive
                && contact.IsPrimary
                && contact.PortalUserId == null
                && contact.Email != null
                && contact.Email.ToLower() == invitation.Email
                && customer.IsActive
                && organization.IsActive
            select new
            {
                OrganizationName = organization.Name,
                contact.FirstName,
                contact.LastName,
                invitation.Email,
                AccountExists = dbContext.Users.Any(user => user.Email == invitation.Email),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new PortalUsableInvitation(
                new PortalInvitationDetails(
                    row.OrganizationName, row.FirstName, row.Email, row.AccountExists, string.IsNullOrWhiteSpace(row.LastName)),
                row.Email);
    }

    public async Task<PortalActivationResult> AcceptNewUserAsync(
        string tokenHash, string passwordHash, string? lastName, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (await LockUsableAsync(tokenHash, now, cancellationToken) is not { } locked)
        {
            return Failed(PortalActivationFailure.Gone);
        }

        if (await dbContext.Users.AsNoTracking().AnyAsync(user => user.Email == locked.Invitation.Email, cancellationToken))
        {
            return Failed(PortalActivationFailure.Ineligible);
        }

        var contactLastName = locked.Contact.LastName;
        var needsLastName = string.IsNullOrWhiteSpace(contactLastName);
        var effectiveLastName = needsLastName ? lastName?.Trim() : contactLastName!.Trim();

        if (string.IsNullOrWhiteSpace(effectiveLastName))
        {
            return Failed(PortalActivationFailure.LastNameRequired);
        }

        var user = User.Create(locked.Invitation.Email, passwordHash, locked.Contact.FirstName, effectiveLastName);
        user.Activate();
        user.MarkEmailVerified(now);
        user.RecordSignIn(now);
        dbContext.Users.Add(user);

        // The same trimmed last name is stored on the contact in the same transaction (BR-13).
        if (needsLastName)
        {
            locked.Contact.SetLastNameIfMissing(effectiveLastName, now);
        }

        return await CompleteAsync(transaction, locked, user, clientIp, now, cancellationToken);
    }

    public async Task<PortalActivationResult> AcceptExistingUserAsync(
        string tokenHash, Guid userId, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (await LockUsableAsync(tokenHash, now, cancellationToken) is not { } locked)
        {
            return Failed(PortalActivationFailure.Gone);
        }

        // The email comes from the database, never from the request; a changed email or status makes the link ineligible.
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null
            || user.Status != UserStatus.Active
            || !string.Equals(user.Email, locked.Invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(PortalActivationFailure.Ineligible);
        }

        var organizationId = locked.Invitation.OrganizationId;

        if (await dbContext.CustomerContacts.AsNoTracking().AnyAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.PortalUserId == userId, cancellationToken))
        {
            return Failed(PortalActivationFailure.Ineligible);
        }

        user.RecordSignIn(now);

        return await CompleteAsync(transaction, locked, user, clientIp, now, cancellationToken);
    }

    public async Task<(Guid UserId, string PasswordHash)?> FindUserCredentialsAsync(
        string normalizedEmail, CancellationToken cancellationToken)
    {
        var row = await dbContext.Users.AsNoTracking()
            .Where(user => user.Email == normalizedEmail)
            .Select(user => new { user.Id, user.PasswordHash })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : (row.Id, row.PasswordHash);
    }

    private async Task<PortalActivationResult> CompleteAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        Locked locked,
        User user,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        locked.Contact.LinkPortal(user.Id, now);
        locked.Invitation.Accept(now);
        dbContext.AuditLogs.Add(AuditLog.Create(
            locked.Invitation.OrganizationId,
            PortalAuditActions.AccessActivated,
            PortalAuditActions.ContactEntityType,
            user.Id,
            locked.Contact.Id,
            ipAddress: clientIp));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The unique indexes (user email, one link per user and organization) are the backstop of a race.
            dbContext.ChangeTracker.Clear();

            return Failed(PortalActivationFailure.Ineligible);
        }

        var links = await new PortalAuthenticationStore(dbContext).GetActiveLinksAsync(user.Id, cancellationToken);
        var current = links.FirstOrDefault(link => link.ContactId == locked.Contact.Id);

        return current is null
            ? Failed(PortalActivationFailure.Gone)
            : new PortalActivationResult.Activated(new PortalAcceptedInvitation(
                new PortalSessionContext(
                    PortalSessionViews.Build(user.FirstName, user.LastName, user.Email, current, links),
                    new PortalScope(current.OrganizationId, current.CustomerId, current.ContactId, user.Id)),
                now));
    }

    // Locks the invitation row by token hash, then rechecks BR-12 under the lock.
    private async Task<Locked?> LockUsableAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Database
            .SqlQuery<Guid>(
                $"""
                SELECT id AS "Value" FROM customer_portal_invitations
                WHERE token_hash = {tokenHash}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        if (ids.Count != 1)
        {
            return null;
        }

        var invitationId = ids[0];
        var invitation = await dbContext.CustomerPortalInvitations.SingleOrDefaultAsync(
            candidate => candidate.Id == invitationId, cancellationToken);

        if (invitation is null || !invitation.IsOpen(now))
        {
            return null;
        }

        var contact = await dbContext.CustomerContacts.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == invitation.OrganizationId && candidate.Id == invitation.ContactId, cancellationToken);

        if (contact is null
            || !contact.IsActive
            || !contact.IsPrimary
            || contact.PortalUserId is not null
            || !string.Equals(contact.Email?.Trim(), invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var contactCustomerId = contact.CustomerId;
        var organizationId = invitation.OrganizationId;
        var active = await (
            from customer in dbContext.Customers.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking() on customer.OrganizationId equals organization.Id
            where customer.OrganizationId == organizationId && customer.Id == contactCustomerId
            select customer.IsActive && organization.IsActive)
            .SingleOrDefaultAsync(cancellationToken);

        return active ? new Locked(invitation, contact) : null;
    }

    private static PortalActivationResult.Failed Failed(PortalActivationFailure failure) => new(failure);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record Locked(CustomerPortalInvitation Invitation, CustomerContact Contact);
}
