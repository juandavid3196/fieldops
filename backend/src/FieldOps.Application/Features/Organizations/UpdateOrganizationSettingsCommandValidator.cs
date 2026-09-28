using FieldOps.Application.Validation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Organization settings field rules (BR-01) plus the <c>updatedAt</c>
/// concurrency token (BR-07). Reuses the same rule builders as organization
/// registration (AS-03); keys are flat (no <c>organization.</c> prefix), per
/// the API contract.
/// </summary>
public sealed class UpdateOrganizationSettingsCommandValidator
    : FieldRulesValidatorBase<UpdateOrganizationSettingsCommand>
{
    public UpdateOrganizationSettingsCommandValidator()
    {
        RequiredTextRule("name", c => c.Name, 160);
        RequiredTextRule("legalName", c => c.LegalName, 200);
        OptionalMaxLengthRule("taxId", c => c.TaxId, 60);
        RequiredEmailRule("email", c => c.Email);
        RequiredPhoneRule("phone", c => c.Phone);
        TimeZoneRule("timezone", c => c.Timezone);
        CurrencyRule("currency", c => c.Currency);
        TaxRateRule("defaultTaxRate", c => c.DefaultTaxRate);
        PrefixRule("quotePrefix", c => c.QuotePrefix);
        PrefixRule("workOrderPrefix", c => c.WorkOrderPrefix);
        PrefixRule("invoicePrefix", c => c.InvoicePrefix);
        NextInvoiceNumberRule("nextInvoiceNumber", c => c.NextInvoiceNumber);
        UpdatedAtRule("updatedAt", c => c.UpdatedAt);
    }
}
