using System.Text.Json.Serialization;
namespace FieldOps.Application.Authentication;

/// <summary>
/// The caller's session as returned by the API: user, resolved organization
/// and the membership role (for display only).
/// </summary>
public sealed record SessionView(
    SessionUser User,
    SessionOrganization Organization,
    SessionRole Role)
{
    /// <summary>True when the organization is a read-only demo; set by the API from server configuration, never stored.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool DemoReadOnly { get; init; }
}

public sealed record SessionUser(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    DateTimeOffset? PhotoUpdatedAt,
    string PreferredLanguage,
    string PreferredTheme);

public sealed record SessionOrganization(Guid Id, string Name);

public sealed record SessionRole(string Code, string Name);
