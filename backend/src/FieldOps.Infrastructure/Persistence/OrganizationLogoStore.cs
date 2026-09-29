using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FieldOps.Infrastructure.Persistence;

internal sealed class OrganizationLogoStore(FieldOpsDbContext dbContext) : IOrganizationLogoStore
{
    public Task<OrganizationLogoContent?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        dbContext.OrganizationLogos.AsNoTracking()
            .Where(logo => logo.OrganizationId == organizationId)
            .Select(logo => new OrganizationLogoContent(logo.ContentType, logo.Content, logo.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<OrganizationLogoMetadata> UpsertAsync(
        Guid organizationId,
        string contentType,
        byte[] content,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            var existing = await dbContext.OrganizationLogos.SingleOrDefaultAsync(
                logo => logo.OrganizationId == organizationId, cancellationToken);

            var before = existing is null ? null : Summarize(existing.ContentType, existing.SizeBytes);
            OrganizationLogo logo;

            if (existing is null)
            {
                logo = OrganizationLogo.Create(organizationId, contentType, content, now);
                dbContext.OrganizationLogos.Add(logo);
            }
            else
            {
                existing.Replace(contentType, content, now);
                logo = existing;
            }

            dbContext.AuditLogs.Add(AuditLog.Create(
                organizationId,
                UploadOrganizationLogoHandler.LogoUpdatedAuditAction,
                "organization",
                actorUserId: actorUserId,
                entityId: organizationId,
                ipAddress: clientIp,
                beforeData: before,
                afterData: Summarize(logo.ContentType, logo.SizeBytes)));

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                return new OrganizationLogoMetadata(logo.ContentType, logo.SizeBytes, logo.UpdatedAt);
            }
            catch (Exception ex) when (attempt == 0 && IsRetryableRace(ex))
            {
                // Two first uploads collided on the primary key, or the row
                // was deleted between the read and the update: start over
                // once from a clean change tracker.
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task<bool> DeleteAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.OrganizationLogos.SingleOrDefaultAsync(
            logo => logo.OrganizationId == organizationId, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        dbContext.OrganizationLogos.Remove(existing);
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            RemoveOrganizationLogoHandler.LogoRemovedAuditAction,
            "organization",
            actorUserId: actorUserId,
            entityId: organizationId,
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
