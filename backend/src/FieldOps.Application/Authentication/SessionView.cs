namespace FieldOps.Application.Authentication;

/// <summary>
/// The caller's session as returned by the API: user, resolved organization
/// and the membership role (for display only).
/// </summary>
public sealed record SessionView(
    SessionUser User,
    SessionOrganization Organization,
    SessionRole Role);

public sealed record SessionUser(
    Guid Id,
    string FirstName,
    string LastName,
    string Email);

public sealed record SessionOrganization(Guid Id, string Name);

public sealed record SessionRole(string Code, string Name);
