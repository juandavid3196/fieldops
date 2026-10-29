namespace FieldOps.Application.Features.DemoMode;

/// <summary>
/// Read-only demo mode: the organizations named in server configuration, and the shared accounts that belong to them,
/// keep their screens and queries but cannot change business data or credentials. Always false when the mode is off.
/// </summary>
public interface IDemoProtection
{
    bool Enabled { get; }

    /// <summary>True when the public slug is one of the configured demo organizations (configuration only, no query).</summary>
    bool IsProtectedSlug(string slug);

    /// <summary>True when the organization is one of the configured demo organizations.</summary>
    Task<bool> IsProtectedOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// True when the user holds a membership in, or is the portal user of a customer of, a demo organization: the
    /// shared credentials of that account must not change.
    /// </summary>
    Task<bool> IsProtectedUserAsync(Guid userId, CancellationToken cancellationToken);
}

public static class DemoModeMessages
{
    public const string Code = "demo_read_only";

    public const string Title = "This demo is read-only.";
}
