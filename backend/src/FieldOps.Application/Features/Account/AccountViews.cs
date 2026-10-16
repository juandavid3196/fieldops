namespace FieldOps.Application.Features.Account;

/// <summary>The session user's profile (account settings FR-05).</summary>
public sealed record AccountProfileView(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    AccountOrganizationView Organization,
    AccountRoleView Role,
    UserPhotoMetadata? Photo);

public sealed record AccountOrganizationView(string Name);

public sealed record AccountRoleView(string Name);

/// <summary>Photo metadata exposed by the profile and by the upload (BR-03).</summary>
public sealed record UserPhotoMetadata(string ContentType, int SizeBytes, DateTimeOffset UpdatedAt);

/// <summary>The stored photo bytes served by <c>GET /account/photo</c> (BR-04).</summary>
public sealed record UserPhotoContent(string ContentType, byte[] Content, DateTimeOffset UpdatedAt);

/// <summary>Validated profile values; the phone is null when empty (BR-01).</summary>
public sealed record AccountProfileValues(string FirstName, string LastName, string? Phone);

/// <summary>Raw profile input of <c>PUT /account/profile</c>.</summary>
public sealed record UpdateAccountProfileCommand(string? FirstName, string? LastName, string? Phone);

/// <summary>The stored credential data needed to verify and compare a password change.</summary>
public sealed record AccountCredential(string PasswordHash, string Email);
