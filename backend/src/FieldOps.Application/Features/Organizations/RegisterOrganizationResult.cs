namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Outcome of an organization registration attempt.
/// </summary>
public abstract record RegisterOrganizationResult
{
    private RegisterOrganizationResult()
    {
    }

    /// <summary>The organization, branch, owner and audit row were created.</summary>
    public sealed record Succeeded(Guid OrganizationId) : RegisterOrganizationResult;

    /// <summary>Field validation failed; keys are the BR-01 dotted paths.</summary>
    public sealed record Invalid(
        IReadOnlyDictionary<string, string[]> Errors) : RegisterOrganizationResult;

    /// <summary>
    /// The normalized owner email already exists in <c>users</c> (BR-19),
    /// found either by the pre-insert check or a unique-constraint race.
    /// </summary>
    public sealed record DuplicateEmail : RegisterOrganizationResult;
}
