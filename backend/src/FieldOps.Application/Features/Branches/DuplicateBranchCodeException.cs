namespace FieldOps.Application.Features.Branches;

/// <summary>
/// Raised when saving a branch loses a concurrent unique-constraint race on
/// <c>branches (organization_id, code)</c> (BR-04), structural clone of
/// <c>DuplicateEmailException</c>. Carries no Postgres dependency: the
/// infrastructure layer translates the database exception into this one.
/// </summary>
public sealed class DuplicateBranchCodeException(Guid organizationId, string code, Exception innerException)
    : Exception("Another branch already uses this code.", innerException)
{
    public Guid OrganizationId { get; } = organizationId;

    public string Code { get; } = code;
}
