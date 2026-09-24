using System.Net;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Sign-in input. It carries no organization, membership, user or role
/// identifier: the organization is always resolved server-side.
/// </summary>
public sealed record SignInCommand(
    string? Email,
    string? Password,
    bool RememberMe,
    IPAddress? ClientIp);
