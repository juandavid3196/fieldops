namespace FieldOps.Application.Features.Organizations;

public abstract record UpdateOrganizationSettingsResult
{
    private UpdateOrganizationSettingsResult()
    {
    }

    public sealed record Succeeded(OrganizationSettingsView Settings) : UpdateOrganizationSettingsResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : UpdateOrganizationSettingsResult;

    public sealed record Stale : UpdateOrganizationSettingsResult;
}
