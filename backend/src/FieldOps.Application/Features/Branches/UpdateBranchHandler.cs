using FieldOps.Application.Auditing;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Validation;
using FieldOps.Domain.Notifications;
using FluentValidation;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Validates and applies a branch update, enforcing the BR-04 pre-check
/// (excluding the branch itself) and BR-07, writing the audit row in the
/// same transaction (FR-08). Inactive branches may be edited.
/// </summary>
public sealed class UpdateBranchHandler(
    IValidator<UpdateBranchCommand> validator,
    IBranchStore store,
    TimeProvider timeProvider)
{
    public const string UpdatedAuditAction = "branch.updated";

    public const string BranchAuditEntityType = "branch";

    public async Task<UpdateBranchResult> HandleAsync(
        UpdateBranchCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new UpdateBranchResult.Invalid(GroupErrors(validation));
        }

        var branch = await store.GetForManageAsync(command.OrganizationId, command.BranchId, cancellationToken);

        if (branch is null)
        {
            return new UpdateBranchResult.NotFound();
        }

        var code = (command.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (await store.CodeExistsAsync(command.OrganizationId, code, command.BranchId, cancellationToken))
        {
            return new UpdateBranchResult.DuplicateCode();
        }

        UpdatedAtValidation.TryParse(command.UpdatedAt, out var requestedUpdatedAt);

        // BR-07 fast path; the UpdatedAt concurrency token still catches a
        // race between this check and SaveChangesAsync.
        if (branch.UpdatedAt != requestedUpdatedAt)
        {
            return new UpdateBranchResult.Stale();
        }

        var beforeFields = CreateBranchHandler.ToFieldMap(branch);

        BusinessHoursValidator.TryValidate(
            command.BusinessHours,
            static (_, _) => { },
            out var businessHoursJson,
            rootKey: "businessHours");

        var email = NullIfEmpty(command.Email) is { } rawEmail ? EmailNormalizer.Normalize(rawEmail) : null;
        var now = timeProvider.GetUtcNow();

        // The validator already guarantees these parse.
        ServicePostalCodes.TryRead(command.ServicePostalCodes, required: true, out var servicePostalCodes, out _);
        ServicePostalCodes.TryReadBoolean(
            command.UsesCompanyBilling, required: true, defaultValue: true, out var usesCompanyBilling, out _);

        branch.UpdateDetails(
            (command.Name ?? string.Empty).Trim(),
            code,
            email,
            NullIfEmpty(command.Phone),
            (command.AddressLine1 ?? string.Empty).Trim(),
            NullIfEmpty(command.AddressLine2),
            (command.City ?? string.Empty).Trim(),
            NullIfEmpty(command.StateRegion),
            (command.PostalCode ?? string.Empty).Trim(),
            (command.CountryCode ?? string.Empty).Trim().ToUpperInvariant(),
            (command.Timezone ?? string.Empty).Trim(),
            businessHoursJson,
            servicePostalCodes,
            usesCompanyBilling,
            now);

        var afterFields = CreateBranchHandler.ToFieldMap(branch);
        var (before, after) = AuditFieldDiff.ForUpdate(beforeFields, afterFields);

        var auditLog = AuditLog.Create(
            command.OrganizationId,
            UpdatedAuditAction,
            BranchAuditEntityType,
            actorUserId: command.ActorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: command.ClientIp,
            beforeData: before,
            afterData: after);

        bool saved;

        try
        {
            saved = await store.TrySaveUpdateAsync(branch, auditLog, cancellationToken);
        }
        catch (DuplicateBranchCodeException)
        {
            return new UpdateBranchResult.DuplicateCode();
        }

        if (!saved)
        {
            return new UpdateBranchResult.Stale();
        }

        return new UpdateBranchResult.Succeeded(CreateBranchHandler.Map(branch));
    }

    private static IReadOnlyDictionary<string, string[]> GroupErrors(
        FluentValidation.Results.ValidationResult validation) =>
        validation.Errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
