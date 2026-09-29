using System.Text.Json;
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

    public const string NextQuoteNumberFloorMessage =
        "Enter a number greater than the last quote number.";

    public const string NextWorkOrderNumberFloorMessage =
        "Enter a number greater than the last work order number.";

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

        var nextQuoteNumber = command.NextQuoteNumber!.Value;
        var nextWorkOrderNumber = command.NextWorkOrderNumber!.Value;
        var pricesIncludeTax = command.PricesIncludeTax!.Value.ValueKind == JsonValueKind.True;

        var floorErrors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var maxInvoiceNumber = await store.GetMaxInvoiceNumberAsync(command.OrganizationId, cancellationToken);

        if (nextInvoiceNumber <= maxInvoiceNumber)
        {
            floorErrors["nextInvoiceNumber"] = [NextInvoiceNumberFloorMessage];
        }

        var maxQuoteNumber = await store.GetMaxQuoteNumberAsync(command.OrganizationId, cancellationToken);

        if (nextQuoteNumber <= maxQuoteNumber)
        {
            floorErrors["nextQuoteNumber"] = [NextQuoteNumberFloorMessage];
        }

        var maxWorkOrderNumber = await store.GetMaxWorkOrderNumberAsync(command.OrganizationId, cancellationToken);

        if (nextWorkOrderNumber <= maxWorkOrderNumber)
        {
            floorErrors["nextWorkOrderNumber"] = [NextWorkOrderNumberFloorMessage];
        }

        if (floorErrors.Count > 0)
        {
            return new UpdateOrganizationSettingsResult.Invalid(floorErrors);
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

        var currency = (command.Currency ?? string.Empty).Trim().ToUpperInvariant();

        // BR-06: an unconfirmed currency change is rejected while invoices exist.
        if (!string.Equals(organization.Currency, currency, StringComparison.Ordinal)
            && !command.ConfirmCurrencyChange
            && await store.HasInvoicesAsync(command.OrganizationId, cancellationToken))
        {
            return new UpdateOrganizationSettingsResult.CurrencyChangeNotConfirmed();
        }

        var beforeFields = ToFieldMap(organization);

        var email = EmailNormalizer.Normalize(command.Email);
        var now = timeProvider.GetUtcNow();
        var countryCode = (command.CountryCode ?? string.Empty).Trim().ToUpperInvariant();
        var stateRegion = UsStates.IsUnitedStates(countryCode)
            ? (command.StateRegion ?? string.Empty).Trim().ToUpperInvariant()
            : NullIfEmpty(command.StateRegion);

        organization.UpdateSettings(
            (command.Name ?? string.Empty).Trim(),
            (command.LegalName ?? string.Empty).Trim(),
            NullIfEmpty(command.TaxId),
            email,
            (command.Phone ?? string.Empty).Trim(),
            (command.Timezone ?? string.Empty).Trim(),
            currency,
            defaultTaxRate,
            (command.QuotePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.WorkOrderPrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.InvoicePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            nextInvoiceNumber,
            nextQuoteNumber,
            nextWorkOrderNumber,
            NullIfEmpty(command.Website),
            (command.AddressLine1 ?? string.Empty).Trim(),
            (command.City ?? string.Empty).Trim(),
            stateRegion,
            (command.PostalCode ?? string.Empty).Trim(),
            countryCode,
            pricesIncludeTax,
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

        return new UpdateOrganizationSettingsResult.Succeeded(
            await GetOrganizationSettingsHandler.MapAsync(store, organization, cancellationToken));
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
            ["nextQuoteNumber"] = organization.NextQuoteNumber,
            ["nextWorkOrderNumber"] = organization.NextWorkOrderNumber,
            ["website"] = organization.Website,
            ["addressLine1"] = organization.AddressLine1,
            ["city"] = organization.City,
            ["stateRegion"] = organization.StateRegion,
            ["postalCode"] = organization.PostalCode,
            ["countryCode"] = organization.CountryCode,
            ["pricesIncludeTax"] = organization.PricesIncludeTax,
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
