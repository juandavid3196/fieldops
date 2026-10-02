using FieldOps.Application.Authentication;
using FieldOps.Application.Features.PublicRequests;
using FieldOps.Domain.Branches;
using FieldOps.Domain.Notifications;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Users;
using FluentValidation;

namespace FieldOps.Application.Features.Organizations;

/// <summary>
/// Validates and creates a new tenant in one transaction: organization,
/// first branch, Owner user, membership, membership-branch link and audit
/// row (FR-04). Anonymous and pre-tenant: no session is created.
/// </summary>
public sealed class RegisterOrganizationHandler(
    IValidator<RegisterOrganizationCommand> validator,
    IOrganizationRegistrationStore store,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    public const int MaxSlugAttempts = 10;

    public const string OwnerRoleCode = "owner";

    public const string RegisteredAuditAction = "organization.registered";

    public const string OrganizationAuditEntityType = "organization";

    public async Task<RegisterOrganizationResult> HandleAsync(
        RegisterOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return new RegisterOrganizationResult.Invalid(validation.Errors
                .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(error => error.ErrorMessage).ToArray(),
                    StringComparer.Ordinal));
        }

        var ownerEmail = EmailNormalizer.Normalize(command.Owner.Email);

        // Pre-insert check: an existing normalized email never reaches the
        // database for this request (FR-06). A concurrent request that wins
        // the unique-constraint race is still caught by the store below.
        if (await store.EmailExistsAsync(ownerEmail, cancellationToken))
        {
            return new RegisterOrganizationResult.DuplicateEmail();
        }

        var ownerRoleId = await store.FindOwnerRoleIdAsync(cancellationToken);

        if (ownerRoleId is null)
        {
            // Propagates to the global exception handler as a 500 with no
            // rows written (FR-09, AC-19): the seeded 'owner' role is
            // expected to always exist.
            throw new InvalidOperationException(
                "The seeded 'owner' role was not found.");
        }

        var passwordHash = passwordHasher.Hash(command.Owner.Password ?? string.Empty);

        BusinessHoursValidator.TryValidate(
            command.Branch.BusinessHours,
            static (_, _) => { },
            out var businessHoursJson);

        // BR-21: lowest free slug computed in memory, then retried with fresh
        // entities when a concurrent registration wins the unique index.
        for (var attempt = 1; ; attempt++)
        {
            var taken = await store.FindSlugsStartingWithAsync(
                PublicSlugGenerator.LookupPrefix(command.Organization.Name), cancellationToken);
            var slug = PublicSlugGenerator.Generate(command.Organization.Name, taken);

            try
            {
                return await SaveAsync(
                    command, ownerEmail, ownerRoleId.Value, passwordHash, businessHoursJson, slug, cancellationToken);
            }
            catch (DuplicatePublicSlugException) when (attempt < MaxSlugAttempts)
            {
            }
        }
    }

    private async Task<RegisterOrganizationResult> SaveAsync(
        RegisterOrganizationCommand command,
        string ownerEmail,
        short ownerRoleId,
        string passwordHash,
        string businessHoursJson,
        string publicSlug,
        CancellationToken cancellationToken)
    {
        var organization = Organization.Create(
            (command.Organization.Name ?? string.Empty).Trim(),
            (command.Organization.LegalName ?? string.Empty).Trim(),
            NullIfEmpty(command.Organization.TaxId),
            EmailNormalizer.Normalize(command.Organization.Email),
            (command.Organization.Phone ?? string.Empty).Trim(),
            (command.Organization.Timezone ?? string.Empty).Trim(),
            (command.Organization.Currency ?? string.Empty).Trim().ToUpperInvariant(),
            command.Organization.DefaultTaxRate ?? 0m,
            (command.Organization.QuotePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.Organization.WorkOrderPrefix ?? string.Empty).Trim().ToUpperInvariant(),
            (command.Organization.InvoicePrefix ?? string.Empty).Trim().ToUpperInvariant(),
            command.Organization.NextInvoiceNumber ?? 1L,
            publicSlug);

        var branchEmail = NullIfEmpty(command.Branch.Email) is { } rawBranchEmail
            ? EmailNormalizer.Normalize(rawBranchEmail)
            : null;

        var branch = Branch.Create(
            organization.Id,
            (command.Branch.Name ?? string.Empty).Trim(),
            (command.Branch.Code ?? string.Empty).Trim().ToUpperInvariant(),
            branchEmail,
            NullIfEmpty(command.Branch.Phone),
            (command.Branch.AddressLine1 ?? string.Empty).Trim(),
            null,
            (command.Branch.City ?? string.Empty).Trim(),
            NullIfEmpty(command.Branch.StateRegion),
            (command.Branch.PostalCode ?? string.Empty).Trim(),
            (command.Branch.CountryCode ?? string.Empty).Trim().ToUpperInvariant(),
            (command.Branch.Timezone ?? string.Empty).Trim(),
            businessHoursJson,
            isMain: true);

        var user = User.Create(
            ownerEmail,
            passwordHash,
            (command.Owner.FirstName ?? string.Empty).Trim(),
            (command.Owner.LastName ?? string.Empty).Trim(),
            NullIfEmpty(command.Owner.Phone));
        user.Activate();

        var membership = OrganizationUser.Create(
            organization.Id,
            user.Id,
            ownerRoleId,
            isAllBranches: true,
            joinedAt: timeProvider.GetUtcNow());

        var membershipBranch = OrganizationUserBranch.Create(membership.Id, branch.Id);

        var auditLog = AuditLog.Create(
            organization.Id,
            RegisteredAuditAction,
            OrganizationAuditEntityType,
            actorUserId: user.Id,
            entityId: organization.Id,
            branchId: branch.Id,
            ipAddress: command.ClientIp);

        try
        {
            await store.SaveRegistrationAsync(
                organization,
                branch,
                user,
                membership,
                membershipBranch,
                auditLog,
                cancellationToken);
        }
        catch (DuplicateEmailException)
        {
            return new RegisterOrganizationResult.DuplicateEmail();
        }

        return new RegisterOrganizationResult.Succeeded(organization.Id);
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
