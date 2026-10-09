using System.Net;
using System.Text.Json;
using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Bank transfer details of an organization (customer-invoice-payments BR-24, BR-25). The write is one conditional UPDATE on
/// <c>bank_details_updated_at</c> (the concurrency value of the details only) plus the audit row in one transaction; it
/// never touches <c>organizations.updated_at</c>, the account number is stored only as ciphertext and the audit row holds
/// no bank data.
/// </summary>
internal sealed class BankDetailsStore(FieldOpsDbContext dbContext) : IBankDetailsStore
{
    public async Task<StoredBankDetails?> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.Organizations.AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new StoredBankDetails(
                organization.BankName,
                organization.BankAccountNumberCiphertext,
                organization.BankAccountLast4,
                organization.BankRoutingNumber,
                organization.BankDetailsUpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> TrySaveAsync(
        Guid organizationId,
        Guid userId,
        IPAddress? ipAddress,
        string bankName,
        byte[]? ciphertext,
        string? accountLast4,
        string routingNumber,
        DateTimeOffset? expected,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // IS NOT DISTINCT FROM: both null while the details are not configured, else the same instant.
        var changed = ciphertext is null
            ? await dbContext.Database.ExecuteSqlAsync(
                $"""
                UPDATE organizations
                SET bank_name = {bankName}, bank_routing_number = {routingNumber}, bank_details_updated_at = {updatedAt}
                WHERE id = {organizationId} AND bank_name IS NOT NULL
                  AND bank_details_updated_at IS NOT DISTINCT FROM {expected}::timestamptz
                """,
                cancellationToken)
            : await dbContext.Database.ExecuteSqlAsync(
                $"""
                UPDATE organizations
                SET bank_name = {bankName}, bank_account_number_ciphertext = {ciphertext}, bank_account_last4 = {accountLast4},
                    bank_routing_number = {routingNumber}, bank_details_updated_at = {updatedAt}
                WHERE id = {organizationId} AND bank_details_updated_at IS NOT DISTINCT FROM {expected}::timestamptz
                """,
                cancellationToken);

        if (changed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        // BR-26: no values at all; the actor and the organization are the audit.
        dbContext.AuditLogs.Add(AuditLog.Create(
            organizationId,
            "organization.bank_details_updated",
            "organization",
            userId,
            organizationId,
            null,
            ipAddress,
            null,
            null,
            JsonSerializer.Serialize(new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
