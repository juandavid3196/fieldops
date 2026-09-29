namespace FieldOps.Api.Contracts;

/// <summary>
/// Bodies of the invitation endpoints. None carries an organization,
/// membership, role, branch or user identifier; unknown properties are ignored.
/// </summary>
public sealed record InvitationTokenRequest(string? Token);

public sealed record AcceptInvitationRequest(
    string? Token,
    string? FirstName,
    string? LastName,
    string? Password);
