using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using FluentValidation;

namespace FieldOps.Application.Authentication;

/// <summary>
/// Signs a user in: validates the input, applies the per-email throttle,
/// verifies the password before any status or membership check, resolves the
/// active organization and records the sign-in with its audit row.
/// </summary>
public sealed class SignInHandler(
    IValidator<SignInCommand> validator,
    ISignInThrottle throttle,
    IPasswordHasher passwordHasher,
    IAuthenticationStore store,
    TimeProvider timeProvider)
{
    public const string SignedInAuditAction = "auth.signed_in";

    public const string UserAuditEntityType = "user";

    public async Task<SignInResult> HandleAsync(
        SignInCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new SignInResult.Invalid(validation.Errors
                .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray(),
                    StringComparer.Ordinal));
        }

        var email = EmailNormalizer.Normalize(command.Email);
        var password = command.Password ?? string.Empty;

        // Checked before any password verification or database access.
        var retryAfter = throttle.GetRetryAfter(email);

        if (retryAfter is not null)
        {
            return new SignInResult.Throttled(retryAfter.Value);
        }

        var user = await store.FindUserByEmailAsync(email, cancellationToken);

        // Always verified, against a dummy hash for unknown emails, so the
        // response time does not reveal whether the email exists.
        var passwordMatches = passwordHasher.Verify(
            password,
            user?.PasswordHash ?? passwordHasher.DummyHash);

        if (user is null)
        {
            return Fail(email, SignInFailureCategory.UnknownEmail, userId: null);
        }

        if (!passwordMatches)
        {
            return Fail(email, SignInFailureCategory.PasswordMismatch, user.Id);
        }

        if (user.Status != UserStatus.Active)
        {
            return Fail(email, SignInFailureCategory.UserNotActive, user.Id);
        }

        var memberships = await store.GetMembershipsAsync(user.Id, cancellationToken);
        var membership = ActiveOrganizationSelector.Select(memberships);

        if (membership is null)
        {
            return Fail(email, SignInFailureCategory.NoEligibleMembership, user.Id);
        }

        var signedInAt = timeProvider.GetUtcNow();

        user.RecordSignIn(signedInAt);

        var auditLog = AuditLog.Create(
            membership.OrganizationId,
            SignedInAuditAction,
            UserAuditEntityType,
            actorUserId: user.Id,
            entityId: user.Id,
            ipAddress: command.ClientIp);

        await store.SaveSignInAsync(user, auditLog, cancellationToken);

        throttle.Reset(email);

        var session = new SessionView(
            new SessionUser(user.Id, user.FirstName, user.LastName, user.Email),
            new SessionOrganization(membership.OrganizationId, membership.OrganizationName),
            new SessionRole(membership.RoleCode, membership.RoleName));

        return new SignInResult.Succeeded(session, membership.MembershipId, signedInAt);
    }

    private SignInResult.InvalidCredentials Fail(
        string email,
        SignInFailureCategory category,
        Guid? userId)
    {
        throttle.RecordFailure(email);

        return new SignInResult.InvalidCredentials(category, userId);
    }
}
