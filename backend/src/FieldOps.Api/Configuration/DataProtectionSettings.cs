namespace FieldOps.Api.Configuration;

/// <summary>
/// ASP.NET Data Protection key ring location, bound from "DataProtection".
/// The session cookies are protected with these keys, so in a deployment the
/// path must be on a persistent volume or every redeploy signs everyone out.
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>Stable application discriminator; changing it invalidates existing cookies.</summary>
    public const string DefaultApplicationName = "FieldOps";

    /// <summary>Directory for the key ring. Empty keeps the framework default store (local runs).</summary>
    public string? KeysPath { get; init; }

    public string ApplicationName { get; init; } = DefaultApplicationName;
}
