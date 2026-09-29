namespace FieldOps.Application.Features.Organizations;

public abstract record UpdateOrganizationSettingsResult
{
    private UpdateOrganizationSettingsResult()
    {
    }

    public sealed record Succeeded(OrganizationSettingsView Settings) : UpdateOrganizationSettingsResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateOrganizationSettingsResult;

    public sealed record Stale : UpdateOrganizationSettingsResult;

    /// <summary>The currency changed while invoices exist and was not confirmed (BR-06).</summary>
    public sealed record CurrencyChangeNotConfirmed : UpdateOrganizationSettingsResult;
}
