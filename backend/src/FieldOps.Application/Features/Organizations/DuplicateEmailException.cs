namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Raised when saving a registration loses a concurrent unique-constraint
/// race on <c>users (email)</c> (FR-06, AC-13). Carries no Postgres
/// dependency: the infrastructure layer translates the database exception
/// into this one.
/// </summary>
public sealed class DuplicateEmailException(string normalizedEmail, Exception innerException)
    : Exception("An account with this email already exists.", innerException)
{
    public string NormalizedEmail { get; } = normalizedEmail;
}
