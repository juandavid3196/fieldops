namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Reads the session organization's settings (FR-03, AC-01, AC-02). No
/// validator: there is no request body to validate.
/// </summary>
public sealed class GetOrganizationSettingsHandler(IOrganizationSettingsStore store)
{
    public async Task<OrganizationSettingsView?> HandleAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await store.GetAsync(organizationId, cancellationToken);

        return organization is null ? null : Map(organization);
    }

    internal static OrganizationSettingsView Map(Domain.Organizations.Organization organization) =>
        new(
            organization.Name,
            organization.LegalName,
            organization.TaxId,
            organization.Email,
            organization.Phone,
            organization.Timezone,
            organization.Currency,
            organization.DefaultTaxRate,
            organization.QuotePrefix,
            organization.WorkOrderPrefix,
            organization.InvoicePrefix,
            organization.NextInvoiceNumber,
            organization.UpdatedAt);
}
