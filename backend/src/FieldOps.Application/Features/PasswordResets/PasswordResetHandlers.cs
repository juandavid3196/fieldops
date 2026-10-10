using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Invitations;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.Users;
using FluentValidation;

namespace FieldOps.Application.Features.PasswordResets;

/// <summary>
/// POST /password-resets (BR-13 step 3): per-email limit, eligibility, token
/// replacement, then enqueue after the commit. Every outcome past validation
/// is the same success so the response never reveals the account.
/// </summary>
public sealed class RequestPasswordResetHandler(
    IValidator<RequestPasswordResetCommand> validator,
    IPasswordResetEmailThrottle throttle,
    IPasswordResetStore store,
    IPasswordResetEmailQueue queue,
    IPasswordResetLinkBuilder linkBuilder)
{
    public async Task<PasswordResetResult<NoValue>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return PasswordResetResult<NoValue>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        var normalizedEmail = EmailNormalizer.Normalize(command.Email);
        var accepted = PasswordResetResult<NoValue>.Ok(default);

        // Counted before the eligibility lookup, whether or not an account exists.
        if (!throttle.TryAcquire(normalizedEmail))
        {
            return accepted;
        }

        var user = await store.FindEligibleUserAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            return accepted;
        }

        // The raw token lives only in this method and the queued message.
        var (rawToken, tokenHash) = InvitationTokens.Generate();

        if (await store.ReplaceAsync(user.UserId, tokenHash, cancellationToken))
        {
            queue.TryEnqueue(new PasswordResetEmail(user.Email, user.FirstName, linkBuilder.BuildResetLink(rawToken)));
        }

        return accepted;
    }
}

/// <summary>
/// POST /portal/password-resets (customer portal BR-15): the rules of <see cref="RequestPasswordResetHandler"/> with the
/// portal eligibility (at least one active link) and the portal reset link.
/// </summary>
public sealed class RequestPortalPasswordResetHandler(
    IValidator<RequestPasswordResetCommand> validator,
    IPasswordResetEmailThrottle throttle,
    IPasswordResetStore store,
    IPasswordResetEmailQueue queue,
    PortalInvitations.IPortalLinkBuilder linkBuilder)
{
    public async Task<PasswordResetResult<NoValue>> HandleAsync(
        RequestPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return PasswordResetResult<NoValue>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        var normalizedEmail = EmailNormalizer.Normalize(command.Email);
        var accepted = PasswordResetResult<NoValue>.Ok(default);

        if (!throttle.TryAcquire(normalizedEmail))
        {
            return accepted;
        }

        var user = await store.FindEligiblePortalUserAsync(normalizedEmail, cancellationToken);

        if (user is null)
        {
            return accepted;
        }

        var (rawToken, tokenHash) = InvitationTokens.Generate();

        if (await store.ReplaceAsync(user.UserId, tokenHash, cancellationToken))
        {
            queue.TryEnqueue(new PasswordResetEmail(user.Email, user.FirstName, linkBuilder.BuildResetLink(rawToken)));
        }

        return accepted;
    }
}

/// <summary>POST /password-resets/validate (BR-07, BR-08).</summary>
public sealed class ValidatePasswordResetHandler(
    IValidator<ValidatePasswordResetCommand> validator,
    IPasswordResetStore store)
{
    public async Task<PasswordResetResult<PasswordResetAccountView>> HandleAsync(
        ValidatePasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return PasswordResetResult<PasswordResetAccountView>.Invalid(
                InvitationHandlerSupport.GroupErrors(validation));
        }

        var account = await store.FindUsableAsync(InvitationTokens.Hash(command.Token!), cancellationToken);

        return account is null
            ? PasswordResetResult<PasswordResetAccountView>.Gone()
            : PasswordResetResult<PasswordResetAccountView>.Ok(account);
    }
}

/// <summary>
/// POST /password-resets/confirm (BR-13 order: field shape, usable token,
/// password versus the account email, then the same checks again under the
/// lock inside the store).
/// </summary>
public sealed class ConfirmPasswordResetHandler(
    IValidator<ConfirmPasswordResetCommand> validator,
    IPasswordResetStore store,
    IPasswordHasher passwordHasher)
{
    public async Task<PasswordResetResult<NoValue>> HandleAsync(
        ConfirmPasswordResetCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return PasswordResetResult<NoValue>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        var tokenHash = InvitationTokens.Hash(command.Token!);
        var account = await store.FindUsableAsync(tokenHash, cancellationToken);

        if (account is null)
        {
            return PasswordResetResult<NoValue>.Gone();
        }

        var password = command.Password!;

        // Only reachable for a usable token, so the email rule never leaks for an unusable one.
        if (string.Equals(password, account.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return PasswordResetResult<NoValue>.Invalid(
                "password",
                RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage);
        }

        // PBKDF2 runs before the transaction so the row lock is held briefly.
        return await store.ConfirmAsync(
            new ConfirmPasswordResetRequest(tokenHash, password, passwordHasher.Hash(password)),
            cancellationToken);
    }
}
