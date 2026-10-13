using System.Net;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Users;
using FluentValidation;

namespace FieldOps.Application.Features.PortalAuth;

internal static class PortalSessions
{
    public static PortalSessionView Build(string firstName, string lastName, string email, PortalLink current, IReadOnlyList<PortalLink> links) =>
        new(
            new PortalSessionUser(firstName, lastName, email),
            new PortalSessionAccount(current.ContactId, current.CustomerName, current.OrganizationName, current.HasLogo),
            links.Select(link => new PortalSessionAccountSummary(link.ContactId, link.CustomerName, link.OrganizationName)).ToList());

    public static PortalScope ScopeOf(PortalLink link) => new(link.OrganizationId, link.CustomerId, link.ContactId, link.UserId);
}

/// <summary>
/// POST /portal/sessions (customer portal BR-03, BR-04): the internal sign-in rules (field validation, per-email throttle and
/// counters, password verified before any status check, dummy hash for unknown emails) with the portal link choice. Every
/// credential failure is the same outcome.
/// </summary>
public sealed class SignInPortalHandler(
    IValidator<SignInCommand> validator,
    ISignInThrottle throttle,
    IPasswordHasher passwordHasher,
    IAuthenticationStore authenticationStore,
    IPortalAuthenticationStore portalStore,
    TimeProvider timeProvider)
{
    public async Task<PortalSignInResult> HandleAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new PortalSignInResult.Invalid(validation.Errors
                .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray(),
                    StringComparer.Ordinal));
        }

        var email = EmailNormalizer.Normalize(command.Email);
        var password = command.Password ?? string.Empty;

        var retryAfter = throttle.GetRetryAfter(email);

        if (retryAfter is not null)
        {
            return new PortalSignInResult.Throttled(retryAfter.Value);
        }

        var user = await authenticationStore.FindUserByEmailAsync(email, cancellationToken);
        var passwordMatches = passwordHasher.Verify(password, user?.PasswordHash ?? passwordHasher.DummyHash);

        if (user is null)
        {
            return Fail(email, SignInFailureCategory.UnknownEmail, null);
        }

        if (!passwordMatches)
        {
            return Fail(email, SignInFailureCategory.PasswordMismatch, user.Id);
        }

        if (user.Status != UserStatus.Active)
        {
            return Fail(email, SignInFailureCategory.UserNotActive, user.Id);
        }

        var links = await portalStore.GetActiveLinksAsync(user.Id, cancellationToken);

        if (links.Count == 0)
        {
            return Fail(email, SignInFailureCategory.NoEligibleMembership, user.Id);
        }

        var chosen = links[0];
        var signedInAt = timeProvider.GetUtcNow();

        user.RecordSignIn(signedInAt);

        // The user update and the audit row are saved together; the cookie is issued only after this commit.
        await authenticationStore.SaveSignInAsync(
            user,
            AuditLog.Create(
                chosen.OrganizationId,
                PortalAuditActions.SignedIn,
                PortalAuditActions.ContactEntityType,
                actorUserId: user.Id,
                entityId: chosen.ContactId,
                ipAddress: command.ClientIp),
            cancellationToken);

        throttle.Reset(email);

        return new PortalSignInResult.Succeeded(
            new PortalSessionContext(
                PortalSessions.Build(user.FirstName, user.LastName, user.Email, chosen, links),
                PortalSessions.ScopeOf(chosen)),
            signedInAt);
    }

    private PortalSignInResult.InvalidCredentials Fail(string email, SignInFailureCategory category, Guid? userId)
    {
        throttle.RecordFailure(email);

        return new PortalSignInResult.InvalidCredentials(category, userId);
    }
}

/// <summary>Per-request revalidation of the portal session (customer portal BR-06).</summary>
public sealed class GetCurrentPortalSessionHandler(IPortalAuthenticationStore store)
{
    public Task<PortalSessionContext?> HandleAsync(
        Guid userId,
        Guid contactId,
        Guid customerId,
        Guid organizationId,
        DateTimeOffset signedInAt,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || contactId == Guid.Empty || customerId == Guid.Empty || organizationId == Guid.Empty)
        {
            return Task.FromResult<PortalSessionContext?>(null);
        }

        return store.FindActiveLinkAsync(userId, contactId, customerId, organizationId, signedInAt, cancellationToken);
    }
}

/// <summary>POST /portal/sessions/current/account (customer portal BR-07): the contact must be one of the caller's active links.</summary>
public sealed class SwitchPortalAccountHandler(IPortalAuthenticationStore store)
{
    public async Task<PortalSessionContext?> HandleAsync(
        PortalScope current,
        DateTimeOffset signedInAt,
        Guid? contactId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        if (contactId is not { } target || target == Guid.Empty)
        {
            return null;
        }

        var link = (await store.GetActiveLinksAsync(current.UserId, cancellationToken))
            .FirstOrDefault(candidate => candidate.ContactId == target);

        if (link is null)
        {
            return null;
        }

        var session = await store.FindActiveLinkAsync(
            current.UserId, link.ContactId, link.CustomerId, link.OrganizationId, signedInAt, cancellationToken);

        if (session is null)
        {
            return null;
        }

        await store.SaveAuditAsync(
            AuditLog.Create(
                link.OrganizationId,
                PortalAuditActions.AccountSwitched,
                PortalAuditActions.ContactEntityType,
                actorUserId: current.UserId,
                entityId: link.ContactId,
                ipAddress: clientIp),
            cancellationToken);

        return session;
    }
}
