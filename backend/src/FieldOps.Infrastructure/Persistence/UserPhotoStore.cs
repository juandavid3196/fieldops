using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Account;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

// Never loads or tracks the User, so users.updated_at is untouched (BR-04).
internal sealed class UserPhotoStore(FieldOpsDbContext dbContext) : IUserPhotoStore
{
    private const string AuditEntity = "user";

    public Task<UserPhotoContent?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.UserPhotos.AsNoTracking()
            .Where(photo => photo.UserId == userId)
            .Select(photo => new UserPhotoContent(photo.ContentType, photo.Content, photo.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<UserPhotoMetadata> UpsertAsync(
        Guid userId,
        Guid organizationId,
        string contentType,
        byte[] content,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var existing = await dbContext.UserPhotos.SingleOrDefaultAsync(
                photo => photo.UserId == userId, cancellationToken);

            var before = existing is null ? null : Summarize(existing.ContentType, existing.SizeBytes);
            UserPhoto photo;

            if (existing is null)
            {
                photo = UserPhoto.Create(userId, contentType, content, now);
                dbContext.UserPhotos.Add(photo);
            }
            else
            {
                existing.Replace(contentType, content, now);
                photo = existing;
            }

            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                UploadUserPhotoHandler.PhotoUpdatedAuditAction,
                AuditEntity,
                actorUserId: userId,
                entityId: userId,
                ipAddress: clientIp,
                beforeData: before,
                afterData: Summarize(photo.ContentType, photo.SizeBytes)));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return new UserPhotoMetadata(photo.ContentType, photo.SizeBytes, photo.UpdatedAt);
            }
            catch (Exception ex) when (attempt == 0 && IsRetryableRace(ex))
            {
                // Two first uploads collided on the primary key, or the row was
                // deleted between the read and the update: start over once.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task<bool> DeleteAsync(
        Guid userId,
        Guid organizationId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.UserPhotos.SingleOrDefaultAsync(
            photo => photo.UserId == userId, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        dbContext.UserPhotos.Remove(existing);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            RemoveUserPhotoHandler.PhotoRemovedAuditAction,
            AuditEntity,
            actorUserId: userId,
            entityId: userId,
            ipAddress: clientIp,
            beforeData: Summarize(existing.ContentType, existing.SizeBytes)));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent delete removed the row first: nothing left to remove.
            dbContext.ChangeTracker.Clear();

            return false;
        }
    }

    // Content type and size only: never the bytes (BR-07).
    private static string Summarize(string contentType, int sizeBytes) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["contentType"] = contentType,
            ["sizeBytes"] = sizeBytes,
        });

    private static bool IsRetryableRace(Exception ex) =>
        ex is DbUpdateConcurrencyException
        || ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } };
}
