namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Reads the session organization's settings (FR-03, FR-07). No validator:
/// there is no request body to validate.
/// </summary>
public sealed class GetOrganizationSettingsHandler(IOrganizationSettingsStore store)
{
    public async Task<OrganizationSettingsView?> HandleAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await store.GetAsync(organizationId, cancellationToken);

        return organization is null
            ? null
            : await MapAsync(store, organization, cancellationToken);
    }

    internal static async Task<OrganizationSettingsView> MapAsync(
        IOrganizationSettingsStore store,
        Domain.Organizations.Organization organization,
        CancellationToken cancellationToken)
    {
        var hasInvoices = await store.HasInvoicesAsync(organization.Id, cancellationToken);
        var logo = await store.GetLogoMetadataAsync(organization.Id, cancellationToken);

        return new(
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
            organization.NextQuoteNumber,
            organization.NextWorkOrderNumber,
            organization.Website,
            organization.AddressLine1,
            organization.City,
            organization.StateRegion,
            organization.PostalCode,
            organization.CountryCode,
            organization.PricesIncludeTax,
            hasInvoices,
            logo,
            organization.UpdatedAt);
    }
}
