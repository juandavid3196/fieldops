namespace FieldOps.Api.Configuration;

/// <summary>
/// Browser origins allowed to call the API, bound from the "Cors" section.
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public const string PolicyName = "FieldOpsClients";

    public string[] AllowedOrigins { get; init; } = [];
}
