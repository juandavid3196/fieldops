using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Account;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

// Every query is keyed by the session user id (and membership for reads); no client identifier reaches the store.
internal sealed class AccountStore(FieldOpsDbContext dbContext) : IAccountStore
{
    private const string AuditEntity = "user";

    // Same join and activity rules as AuthenticationStore.FindActiveSessionAsync, left-joined to user_photos.
    public Task<AccountProfileView?> GetProfileAsync(
        Guid userId, Guid organizationId, Guid membershipId, CancellationToken cancellationToken) =>
        (
            from membership in dbContext.OrganizationUsers.AsNoTracking()
            join user in dbContext.Users.AsNoTracking()
                on membership.UserId equals user.Id
            join organization in dbContext.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            join role in dbContext.Roles.AsNoTracking()
                on membership.RoleId equals role.Id
            join photo in dbContext.UserPhotos.AsNoTracking()
                on user.Id equals photo.UserId into photos
            from photo in photos.DefaultIfEmpty()
            where membership.Id == membershipId
                && membership.UserId == userId
                && membership.OrganizationId == organizationId
                && membership.Status == UserStatus.Active
                && user.Status == UserStatus.Active
                && organization.IsActive
            select new AccountProfileView(
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone,
                new AccountOrganizationView(organization.Name),
                new AccountRoleView(role.Name),
                photo == null ? null : new UserPhotoMetadata(photo.ContentType, photo.SizeBytes, photo.UpdatedAt)))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AccountProfileView?> UpdateProfileAsync(
        Guid userId,
        Guid organizationId,
        Guid membershipId,
        AccountProfileValues values,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var changed = new List<string>();

        if (!string.Equals(user.FirstName, values.FirstName, StringComparison.Ordinal))
        {
            changed.Add(AccountProfileRules.FirstNameKey);
        }

        if (!string.Equals(user.LastName, values.LastName, StringComparison.Ordinal))
        {
            changed.Add(AccountProfileRules.LastNameKey);
        }

        if (!string.Equals(user.Phone, values.Phone, StringComparison.Ordinal))
        {
            changed.Add(AccountProfileRules.PhoneKey);
        }

        if (changed.Count > 0)
        {
            user.UpdateProfile(values.FirstName, values.LastName, values.Phone, now);

            // Changed field names only, never values (BR-07).
            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                UpdateAccountProfileHandler.ProfileUpdatedAuditAction,
                AuditEntity,
                actorUserId: userId,
                entityId: userId,
                ipAddress: clientIp,
                metadata: JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["changedFields"] = changed,
                })));

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetProfileAsync(userId, organizationId, membershipId, cancellationToken);
    }

    public Task<AccountCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AccountCredential(user.PasswordHash, user.Email))
            .SingleOrDefaultAsync(cancellationToken);

    // One SaveChangesAsync: the user update and the audit insert commit together or not at all.
    public async Task ChangePasswordAsync(
        Guid userId,
        Guid organizationId,
        string passwordHash,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == userId, cancellationToken);

        user.ChangePassword(passwordHash, now);

        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            ChangePasswordHandler.PasswordChangedAuditAction,
            AuditEntity,
            actorUserId: userId,
            entityId: userId,
            ipAddress: clientIp));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
