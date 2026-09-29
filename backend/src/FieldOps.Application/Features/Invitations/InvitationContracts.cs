using System.Net;
using FieldOps.Application.Authentication;

namespace FieldOps.Application.Features.Invitations;

/// <summary>
/// Inputs carry only the token (and, for a new account, the confirmed profile
/// and password): the organization, role, branches and email always come from
/// the invitation the token resolves to.
/// </summary>
public sealed record ValidateInvitationCommand(string? Token);

public sealed record AcceptInvitationCommand(
    string? Token,
    string? FirstName,
    string? LastName,
    string? Password,
    IPAddress? ClientIp);

public sealed record AcceptExistingInvitationCommand(
    string? Token,
    Guid UserId,
    IPAddress? ClientIp);

/// <summary>The only invitation data a token holder may read (BR-04); no ids.</summary>
public sealed record InvitationDetailsView(
    string OrganizationName,
    string InviterName,
    string Email,
    string FirstName,
    string LastName,
    InvitationRoleView Role,
    bool IsAllBranches,
    IReadOnlyList<InvitationBranchView> Branches,
    DateTimeOffset ExpiresAt);

public sealed record InvitationRoleView(string Code, string Name);

public sealed record InvitationBranchView(string Name);

/// <summary>A committed acceptance: the session to issue and its membership.</summary>
public sealed record AcceptedInvitation(
    SessionView Session,
    Guid MembershipId,
    DateTimeOffset SignedInAt);

public enum InvitationResultKind
{
    Succeeded,
    Invalid,
    Gone,
    Conflict,
}

public enum InvitationConflict
{
    AccountExists,
    IdentityMismatch,
    MembershipExists,
    AccessUnavailable,
}

/// <summary>Outcome of an invitation use case, mapped to HTTP by the controller.</summary>
public sealed record InvitationResult<T>(
    InvitationResultKind Kind,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    InvitationConflict? Conflict = null)
{
    public static InvitationResult<T> Ok(T value) => new(InvitationResultKind.Succeeded, value);

    public static InvitationResult<T> Gone() => new(InvitationResultKind.Gone);

    public static InvitationResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(InvitationResultKind.Invalid, Errors: errors);

    public static InvitationResult<T> Invalid(string key, string message) =>
        Invalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] });

    public static InvitationResult<T> Failed(InvitationConflict conflict) =>
        new(InvitationResultKind.Conflict, Conflict: conflict);
}

public sealed record AcceptNewUserRequest(
    string TokenHash,
    string FirstName,
    string LastName,
    string PasswordHash,
    IPAddress? ClientIp);

public sealed record AcceptExistingUserRequest(
    string TokenHash,
    Guid UserId,
    IPAddress? ClientIp);

/// <summary>
/// Persistence for invitation acceptance. Each accept call is one transaction
/// that locks the invitation row and re-checks BR-02 under the lock.
/// </summary>
public interface IInvitationAcceptanceStore
{
    /// <summary>Read-only; null unless the invitation is usable (BR-02).</summary>
    Task<InvitationDetailsView?> FindUsableAsync(string tokenHash, CancellationToken cancellationToken);

    Task<InvitationResult<AcceptedInvitation>> AcceptNewUserAsync(
        AcceptNewUserRequest request,
        CancellationToken cancellationToken);

    Task<InvitationResult<AcceptedInvitation>> AcceptExistingUserAsync(
        AcceptExistingUserRequest request,
        CancellationToken cancellationToken);
}
