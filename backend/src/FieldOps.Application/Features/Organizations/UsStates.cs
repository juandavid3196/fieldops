namespace FieldOps.Application.Features.Organizations;

/// <summary>The 50 US states plus DC (BR-04), by two-letter code.</summary>
public static class UsStates
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL",
        "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME",
        "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH",
        "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI",
        "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI",
        "WY",
    };

    public static bool IsUnitedStates(string? countryCode) =>
        string.Equals(countryCode?.Trim(), "US", StringComparison.OrdinalIgnoreCase);
}
