using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.DemoMode;

/// <summary>
/// <c>DemoMode</c> settings: off by default. When enabled, the organizations whose public slug is listed (and their
/// shared accounts) are read-only on the API. Set on the server only; the client never sends a demo flag.
/// </summary>
public sealed class DemoModeSettings
{
    public const string SectionName = "DemoMode";

    public bool Enabled { get; set; }

    public string[] OrganizationSlugs { get; set; } = [];
}

/// <summary>Rejects an enabled demo mode that protects nothing, so a misspelt key fails on start instead of leaving data writable.</summary>
public sealed class DemoModeSettingsValidator : IValidateOptions<DemoModeSettings>
{
    public ValidateOptionsResult Validate(string? name, DemoModeSettings options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return options.OrganizationSlugs.Any(slug => !string.IsNullOrWhiteSpace(slug))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{DemoModeSettings.SectionName}:OrganizationSlugs' needs at least one slug when '{DemoModeSettings.SectionName}:Enabled' is true.");
    }
}
