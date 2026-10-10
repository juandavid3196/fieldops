using System.Net;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Access;
using FieldOps.Application.Features.Customers;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using FieldOps.Application.Features.Users;

namespace FieldOps.Application.Features.PortalInvitations;

/// <summary>Staff: POST /customers/{id}/portal-invitation (customer portal BR-11). Visibility follows the customer branch scope.</summary>
public sealed class InvitePortalContactHandler(
    IBranchScopeResolver scopes,
    ICustomerDetailStore detailStore,
    IPortalInvitationStore store,
    IPortalLinkBuilder links,
    IPortalInvitationNotifier notifier)
{
    public async Task<PortalOutcome<PortalStatusView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid userId,
        Guid customerId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return new PortalOutcome<PortalStatusView>.NotFound();
        }

        // The raw token lives only in this method and the email after the commit.
        var (raw, hash) = InvitationTokens.Generate();

        switch (await store.InviteAsync(organizationId, customerId, userId, raw, hash, clientIp, cancellationToken))
        {
            case PortalInviteResult.Sent sent:
                await notifier.SendAsync(sent.Email, links.BuildActivationLink(raw), cancellationToken);

                return new PortalOutcome<PortalStatusView>.Ok(sent.Status);
            case PortalInviteResult.Unavailable:
                return new PortalOutcome<PortalStatusView>.Conflict(
                    PortalMessages.InviteUnavailableCode, PortalMessages.InviteUnavailableTitle);
            default:
                return new PortalOutcome<PortalStatusView>.NotFound();
        }
    }
}

/// <summary>Staff: DELETE /customers/{id}/portal-access (customer portal BR-14).</summary>
public sealed class RemovePortalAccessHandler(
    IBranchScopeResolver scopes,
    ICustomerDetailStore detailStore,
    IPortalInvitationStore store)
{
    public async Task<PortalOutcome<PortalStatusView>> HandleAsync(
        Guid organizationId,
        Guid membershipId,
        Guid userId,
        Guid customerId,
        IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveAsync(organizationId, membershipId, cancellationToken);

        if (!await detailStore.IsCustomerVisibleAsync(organizationId, scope, customerId, cancellationToken))
        {
            return new PortalOutcome<PortalStatusView>.NotFound();
        }

        return await store.RemoveAccessAsync(organizationId, customerId, userId, clientIp, cancellationToken) is { } status
            ? new PortalOutcome<PortalStatusView>.Ok(status)
            : new PortalOutcome<PortalStatusView>.NotFound();
    }
}

/// <summary>POST /portal/invitations/validate (customer portal BR-12): one identical gone outcome for every unusable token.</summary>
public sealed class ValidatePortalInvitationHandler(IPortalInvitationStore store)
{
    public async Task<PortalOutcome<PortalInvitationDetails>> HandleAsync(
        ValidatePortalInvitationCommand command, CancellationToken cancellationToken)
    {
        if (!InvitationTokenRules.IsWellFormed(command.Token))
        {
            return PortalOutcome<PortalInvitationDetails>.Failure(InvitationTokenRules.TokenKey, InvitationTokenRules.TokenInvalidMessage);
        }

        return await store.FindUsableAsync(InvitationTokens.Hash(command.Token!), cancellationToken) is { } usable
            ? new PortalOutcome<PortalInvitationDetails>.Ok(usable.Details)
            : new PortalOutcome<PortalInvitationDetails>.Gone();
    }
}

internal static class PortalActivationSupport
{
    public const string LastNameRequiredMessage = "Enter your last name.";

    public const string LastNameTooLongMessage = "Last name must be 100 characters or fewer.";
}

/// <summary>POST /portal/invitations/accept (customer portal BR-13): a new user, with the last name when the contact has none.</summary>
public sealed class AcceptPortalInvitationHandler(
    IPortalInvitationStore store,
    IPasswordHasher passwordHasher)
{
    public async Task<PortalOutcome<PortalAcceptedInvitation>> HandleAsync(
        AcceptPortalInvitationCommand command, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!InvitationTokenRules.IsWellFormed(command.Token))
        {
            errors[InvitationTokenRules.TokenKey] = [InvitationTokenRules.TokenInvalidMessage];
        }

        var password = command.Password ?? string.Empty;

        if (password.Length == 0)
        {
            errors["password"] = ["This field is required."];
        }
        else if (password.Length is < AcceptInvitationCommandValidator.PasswordMinLength or > AcceptInvitationCommandValidator.PasswordMaxLength)
        {
            errors["password"] = [RegisterOrganizationCommandValidator.PasswordLengthMessage];
        }

        if (errors.Count > 0)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Invalid(errors);
        }

        var tokenHash = InvitationTokens.Hash(command.Token!);

        if (await store.FindUsableAsync(tokenHash, cancellationToken) is not { } usable)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Gone();
        }

        // The last name is only read when the contact has none; otherwise the value is ignored.
        string? lastName = null;

        if (usable.Details.LastNameRequired)
        {
            lastName = (command.LastName ?? string.Empty).Trim();

            if (lastName.Length == 0)
            {
                return PortalOutcome<PortalAcceptedInvitation>.Failure("lastName", PortalActivationSupport.LastNameRequiredMessage);
            }

            if (lastName.Length > AcceptInvitationCommandValidator.NameMaxLength)
            {
                return PortalOutcome<PortalAcceptedInvitation>.Failure("lastName", PortalActivationSupport.LastNameTooLongMessage);
            }
        }

        if (string.Equals(password, usable.Details.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return PortalOutcome<PortalAcceptedInvitation>.Failure(
                "password", RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage);
        }

        // An account with the email already exists: the existing-account endpoint is the only way to link it.
        if (usable.Details.AccountExists)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Conflict(
                PortalMessages.InvitationIneligibleCode, PortalMessages.InvitationIneligibleTitle);
        }

        var result = await store.AcceptNewUserAsync(
            tokenHash, passwordHasher.Hash(password), lastName, command.ClientIp, cancellationToken);

        return result switch
        {
            PortalActivationResult.Activated activated => new PortalOutcome<PortalAcceptedInvitation>.Ok(activated.Accepted),
            PortalActivationResult.Failed { Failure: PortalActivationFailure.Ineligible } => new PortalOutcome<PortalAcceptedInvitation>.Conflict(
                PortalMessages.InvitationIneligibleCode, PortalMessages.InvitationIneligibleTitle),
            PortalActivationResult.Failed { Failure: PortalActivationFailure.LastNameRequired } =>
                PortalOutcome<PortalAcceptedInvitation>.Failure("lastName", PortalActivationSupport.LastNameRequiredMessage),
            _ => new PortalOutcome<PortalAcceptedInvitation>.Gone(),
        };
    }
}

/// <summary>
/// POST /portal/invitations/accept-existing (customer portal BR-13): the existing user proves the password; a wrong one is a
/// generic 401 counted by the sign-in throttle. It never asks for or changes a last name.
/// </summary>
public sealed class AcceptExistingPortalInvitationHandler(
    IPortalInvitationStore store,
    IPasswordHasher passwordHasher,
    ISignInThrottle throttle)
{
    public async Task<PortalOutcome<PortalAcceptedInvitation>> HandleAsync(
        AcceptExistingPortalInvitationCommand command, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (!InvitationTokenRules.IsWellFormed(command.Token))
        {
            errors[InvitationTokenRules.TokenKey] = [InvitationTokenRules.TokenInvalidMessage];
        }

        var password = command.Password ?? string.Empty;

        if (password.Length == 0)
        {
            errors["password"] = [SignInCommandValidator.PasswordRequiredMessage];
        }
        else if (password.Length > SignInCommandValidator.PasswordMaxLength)
        {
            errors["password"] = [SignInCommandValidator.PasswordTooLongMessage];
        }

        if (errors.Count > 0)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Invalid(errors);
        }

        var tokenHash = InvitationTokens.Hash(command.Token!);

        if (await store.FindUsableAsync(tokenHash, cancellationToken) is not { } usable)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Gone();
        }

        var email = usable.NormalizedEmail;

        if (throttle.GetRetryAfter(email) is { } retryAfter)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Throttled(retryAfter);
        }

        var credentials = await store.FindUserCredentialsAsync(email, cancellationToken);

        // Always verified (dummy hash when there is no account) so the time does not reveal the account.
        var matches = passwordHasher.Verify(password, credentials?.PasswordHash ?? passwordHasher.DummyHash);

        if (credentials is null)
        {
            return new PortalOutcome<PortalAcceptedInvitation>.Conflict(
                PortalMessages.InvitationIneligibleCode, PortalMessages.InvitationIneligibleTitle);
        }

        if (!matches)
        {
            throttle.RecordFailure(email);

            return new PortalOutcome<PortalAcceptedInvitation>.Unauthorized();
        }

        var result = await store.AcceptExistingUserAsync(tokenHash, credentials.Value.UserId, command.ClientIp, cancellationToken);

        if (result is PortalActivationResult.Activated activated)
        {
            throttle.Reset(email);

            return new PortalOutcome<PortalAcceptedInvitation>.Ok(activated.Accepted);
        }

        return result is PortalActivationResult.Failed { Failure: PortalActivationFailure.Ineligible }
            ? new PortalOutcome<PortalAcceptedInvitation>.Conflict(
                PortalMessages.InvitationIneligibleCode, PortalMessages.InvitationIneligibleTitle)
            : new PortalOutcome<PortalAcceptedInvitation>.Gone();
    }
}
