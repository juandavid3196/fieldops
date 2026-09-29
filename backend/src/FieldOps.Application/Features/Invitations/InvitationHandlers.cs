using FieldOps.Application.Authentication;
using FieldOps.Application.Features.Organizations;
using FieldOps.Application.Features.Users;
using FluentValidation;
using FluentValidation.Results;

namespace FieldOps.Application.Features.Invitations;

internal static class InvitationHandlerSupport
{
    public static IReadOnlyDictionary<string, string[]> GroupErrors(ValidationResult validation) =>
        validation.Errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray(),
                StringComparer.Ordinal);
}

/// <summary>POST /invitations/validate (BR-03 to BR-05).</summary>
public sealed class ValidateInvitationHandler(
    IValidator<ValidateInvitationCommand> validator,
    IInvitationAcceptanceStore store)
{
    public async Task<InvitationResult<InvitationDetailsView>> HandleAsync(
        ValidateInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return InvitationResult<InvitationDetailsView>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        var details = await store.FindUsableAsync(InvitationTokens.Hash(command.Token!), cancellationToken);

        return details is null
            ? InvitationResult<InvitationDetailsView>.Gone()
            : InvitationResult<InvitationDetailsView>.Ok(details);
    }
}

/// <summary>
/// POST /invitations/accept (BR-18 order: field shape, usable token, password
/// versus the invitation email, then eligibility under the lock).
/// </summary>
public sealed class AcceptInvitationHandler(
    IValidator<AcceptInvitationCommand> validator,
    IInvitationAcceptanceStore store,
    IPasswordHasher passwordHasher)
{
    public async Task<InvitationResult<AcceptedInvitation>> HandleAsync(
        AcceptInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return InvitationResult<AcceptedInvitation>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        var tokenHash = InvitationTokens.Hash(command.Token!);
        var details = await store.FindUsableAsync(tokenHash, cancellationToken);

        if (details is null)
        {
            return InvitationResult<AcceptedInvitation>.Gone();
        }

        var password = command.Password ?? string.Empty;

        // Only reachable for a usable token, so the email is never revealed
        // for an unusable one.
        if (string.Equals(password, details.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return InvitationResult<AcceptedInvitation>.Invalid(
                "password",
                RegisterOrganizationCommandValidator.PasswordEqualsEmailMessage);
        }

        return await store.AcceptNewUserAsync(
            new AcceptNewUserRequest(
                tokenHash,
                (command.FirstName ?? string.Empty).Trim(),
                (command.LastName ?? string.Empty).Trim(),
                passwordHasher.Hash(password),
                command.ClientIp),
            cancellationToken);
    }
}

/// <summary>POST /invitations/accept-existing; the caller already holds a valid session.</summary>
public sealed class AcceptExistingInvitationHandler(
    IValidator<AcceptExistingInvitationCommand> validator,
    IInvitationAcceptanceStore store)
{
    public async Task<InvitationResult<AcceptedInvitation>> HandleAsync(
        AcceptExistingInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return InvitationResult<AcceptedInvitation>.Invalid(InvitationHandlerSupport.GroupErrors(validation));
        }

        return await store.AcceptExistingUserAsync(
            new AcceptExistingUserRequest(
                InvitationTokens.Hash(command.Token!),
                command.UserId,
                command.ClientIp),
            cancellationToken);
    }
}
