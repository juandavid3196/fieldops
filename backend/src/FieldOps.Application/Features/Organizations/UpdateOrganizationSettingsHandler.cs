using FieldOps.Application.Auditing;
using FieldOps.Application.Authentication;
using FieldOps.Application.Validation;
using FieldOps.Domain.Notifications;
using FluentValidation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Validates and applies an organization settings update in one transaction:
/// entity update and audit row (FR-04).
/// </summary>
public sealed class UpdateOrganizationSettingsHandler(
    IValidator<UpdateOrganizationSettingsCommand> validator,
    IOrganizationSettingsStore store,
    TimeProvider timeProvider)
{
    public const string SettingsUpdatedAuditAction = "organization.settings_updated";

    public const string OrganizationAuditEntityType = "organization";

    public const string NextInvoiceNumberFloorMessage =
        "Enter a number greater than the last invoice number.";

    public async Task<UpdateOrganizationSettingsResult> HandleAsync(
        UpdateOrganizationSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new UpdateOrganizationSettingsResult.Invalid(GroupErrors(validation));
        }

        // The validator already guarantees these parse/exist.
        var nextInvoiceNumber = command.NextInvoiceNumber!.Value;
        var defaultTaxRate = command.DefaultTaxRate!.Value;
        UpdatedAtValidation.TryParse(command.UpdatedAt, out var requestedUpdatedAt);

        var maxInvoiceNumber = await store.GetMaxInvoiceNumberAsync(command.OrganizationId, cancellationToken);

        if (nextInvoiceNumber <= maxInvoiceNumber)
        {
            return new UpdateOrganizationSettingsResult.Invalid(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["nextInvoiceNumber"] = [NextInvoiceNumberFloorMessage],
                });
        }

        var organization = await store.GetAsync(command.OrganizationId, cancellationToken);

        if (organization is null)
        {
            throw new InvalidOperationException(
                "The session organization was not found.");
        }

        // BR-07 fast path: compares the client's submitted value against the
        // freshly loaded row before touching anything. A race between this
        // check and SaveChangesAsync is still caught by the UpdatedAt
        // concurrency token below.
        if (organization.UpdatedAt != requestedUpdatedAt)
        {
            return new UpdateOrganizationSettingsResult.Stale();
        }

        var beforeFields = ToFieldMap(organization);

        var email = EmailNormalizer.Normalize(command.Email);
        var now = timeProvider.GetUtcNow();

        organization.UpdateSettings(
            (command.Name ?? string.Empty).Trim(),
            (command.LegalName ?? string.Empty).Trim(),
            NullIfEmpty(command.TaxId),
            email,
            (command.Phone ?? string.Empty).Trim(),
            (command.Timezone ?? string.Empty).Trim(),
            (command.Currency ?? string.Empty).Trim().ToUpperInvariant(),
            defaultTaxRate,
            (command.QuotePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.WorkOrderPrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.InvoicePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            nextInvoiceNumber,
            now);

        var afterFields = ToFieldMap(organization);
        var (before, after) = AuditFieldDiff.ForUpdate(beforeFields, afterFields);

        var auditLog = AuditLog.Create(
            command.OrganizationId,
            SettingsUpdatedAuditAction,
            OrganizationAuditEntityType,
            actorUserId: command.ActorUserId,
            entityId: command.OrganizationId,
            branchId: null,
            ipAddress: command.ClientIp,
            beforeData: before,
            afterData: after);

        var saved = await store.TrySaveUpdateAsync(organization, auditLog, cancellationToken);

        if (!saved)
        {
            return new UpdateOrganizationSettingsResult.Stale();
        }

        return new UpdateOrganizationSettingsResult.Succeeded(GetOrganizationSettingsHandler.Map(organization));
    }

    private static Dictionary<string, object?> ToFieldMap(Domain.Organizations.Organization organization) =>
        new(StringComparer.Ordinal)
        {
            ["name"] = organization.Name,
            ["legalName"] = organization.LegalName,
            ["taxId"] = organization.TaxId,
            ["email"] = organization.Email,
            ["phone"] = organization.Phone,
            ["timezone"] = organization.Timezone,
            ["currency"] = organization.Currency,
            ["defaultTaxRate"] = organization.DefaultTaxRate,
            ["quotePrefix"] = organization.QuotePrefix,
            ["workOrderPrefix"] = organization.WorkOrderPrefix,
            ["invoicePrefix"] = organization.InvoicePrefix,
            ["nextInvoiceNumber"] = organization.NextInvoiceNumber,
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
