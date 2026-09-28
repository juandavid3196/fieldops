using FieldOps.Application.Auditing;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Organizations;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FluentValidation;

namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Validates and creates a branch, enforcing the BR-05 cap and the BR-04
/// pre-check, and writing the audit row in the same transaction (FR-07).
/// </summary>
public sealed class CreateBranchHandler(
    IValidator<CreateBranchCommand> validator,
    IBranchStore store)
{
    public const int BranchLimit = 100;

    public const string CreatedAuditAction = "branch.created";

    public const string BranchAuditEntityType = "branch";

    public async Task<CreateBranchResult> HandleAsync(
        CreateBranchCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new CreateBranchResult.Invalid(GroupErrors(validation));
        }

        var count = await store.CountAsync(command.OrganizationId, cancellationToken);

        if (count >= BranchLimit)
        {
            return new CreateBranchResult.LimitReached();
        }

        var code = (command.Code ?? string.Empty).Trim().ToUpperInvariant();

        if (await store.CodeExistsAsync(command.OrganizationId, code, excludeBranchId: null, cancellationToken))
        {
            return new CreateBranchResult.DuplicateCode();
        }

        BusinessHoursValidator.TryValidate(
            command.BusinessHours,
            static (_, _) => { },
            out var businessHoursJson,
            rootKey: "businessHours");

        var email = NullIfEmpty(command.Email) is { } rawEmail ? EmailNormalizer.Normalize(rawEmail) : null;

        var branch = Branch.Create(
            command.OrganizationId,
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
            businessHoursJson);

        var afterFields = ToFieldMap(branch);
        var (before, after) = AuditFieldDiff.ForCreate(afterFields);

        var auditLog = AuditLog.Create(
            command.OrganizationId,
            CreatedAuditAction,
            BranchAuditEntityType,
            actorUserId: command.ActorUserId,
            entityId: branch.Id,
            branchId: branch.Id,
            ipAddress: command.ClientIp,
            beforeData: before,
            afterData: after);

        try
        {
            await store.CreateAsync(branch, auditLog, cancellationToken);
        }
        catch (DuplicateBranchCodeException)
        {
            return new CreateBranchResult.DuplicateCode();
        }

        return new CreateBranchResult.Succeeded(Map(branch));
    }

    internal static BranchDetailView Map(Branch branch) =>
        new(
            branch.Id,
            branch.Name,
            branch.Code,
            branch.Email,
            branch.Phone,
            branch.AddressLine1,
            branch.AddressLine2,
            branch.City,
            branch.StateRegion,
            branch.PostalCode,
            branch.CountryCode,
            branch.Timezone,
            branch.BusinessHours,
            branch.IsActive,
            branch.UpdatedAt);

    internal static Dictionary<string, object?> ToFieldMap(Branch branch) =>
        new(StringComparer.Ordinal)
        {
            ["name"] = branch.Name,
            ["code"] = branch.Code,
            ["phone"] = branch.Phone,
            ["email"] = branch.Email,
            ["timezone"] = branch.Timezone,
            ["addressLine1"] = branch.AddressLine1,
            ["addressLine2"] = branch.AddressLine2,
            ["city"] = branch.City,
            ["stateRegion"] = branch.StateRegion,
            ["postalCode"] = branch.PostalCode,
            ["countryCode"] = branch.CountryCode,
            ["businessHours"] = branch.BusinessHours,
        };

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
