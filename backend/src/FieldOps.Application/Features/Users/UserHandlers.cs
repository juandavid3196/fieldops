using System.Net;
using FieldOps.Application.Authentication;
using FluentValidation;

namespace FieldOps.Application.Features.Users;

/// <summary>GET /users (FR-02): validates the BR-03 query and pages the store result.</summary>
public sealed class ListUsersHandler(IUserAccessStore store)
{
    public const int FixedPageSize = 10;

    public const int SearchMaxLength = 100;

    public async Task<UserResult<UserListPageView>> HandleAsync(
        ListUsersQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var search = (query.Search ?? string.Empty).Trim();

        if (search.Length > SearchMaxLength)
        {
            errors["search"] = ["Use 100 characters or fewer."];
        }

        var roleCode = string.IsNullOrWhiteSpace(query.RoleCode) ? null : query.RoleCode.Trim();

        if (roleCode is not null && !PermissionCatalog.IsRoleCode(roleCode))
        {
            errors["roleCode"] = ["Select a valid role."];
        }

        Guid? branchId = null;

        if (!string.IsNullOrWhiteSpace(query.BranchId))
        {
            if (Guid.TryParse(query.BranchId, out var parsed) && parsed != Guid.Empty)
            {
                branchId = parsed;
            }
            else
            {
                errors["branchId"] = [UserMessages.InvalidBranch];
            }
        }

        var status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim();

        if (status is not null
            && status is not (UserRowStatuses.Active or UserRowStatuses.Suspended or UserRowStatuses.PendingInvitation))
        {
            errors["status"] = ["Select a valid status."];
        }

        var sort = string.IsNullOrWhiteSpace(query.Sort) ? "name" : query.Sort.Trim();

        if (sort is not ("name" or "-name"))
        {
            errors["sort"] = ["Select a valid sort."];
        }

        var page = 1;

        if (!string.IsNullOrWhiteSpace(query.Page)
            && (!int.TryParse(query.Page, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out page)
                || page < 1))
        {
            errors["page"] = ["Enter a valid page."];
        }

        if (!string.IsNullOrWhiteSpace(query.PageSize)
            && (!int.TryParse(query.PageSize, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var pageSize)
                || pageSize != FixedPageSize))
        {
            errors["pageSize"] = ["Page size must be 10."];
        }

        if (errors.Count > 0)
        {
            return UserResult<UserListPageView>.Invalid(errors);
        }

        var filter = new UserListFilter(
            search.Length == 0 ? null : search,
            roleCode,
            branchId,
            status,
            Descending: sort == "-name",
            page,
            FixedPageSize);

        return UserResult<UserListPageView>.Ok(
            await store.ListAsync(query.OrganizationId, query.CurrentMembershipId, filter, cancellationToken));
    }
}

public sealed class GetUsersSummaryHandler(IUserAccessStore store)
{
    public Task<UserSummaryView> HandleAsync(Guid organizationId, CancellationToken cancellationToken) =>
        store.GetSummaryAsync(organizationId, cancellationToken);
}

public sealed class GetPermissionMatrixHandler
{
    public (IReadOnlyList<RoleDefinition> Roles, IReadOnlyList<ModuleDefinition> Modules) Handle() =>
        (PermissionCatalog.Roles, PermissionCatalog.Modules);
}

/// <summary>POST /users/invitations (FR-06, BR-08 to BR-11).</summary>
public sealed class InviteUserHandler(
    IValidator<InviteUserCommand> validator,
    IUserAccessStore store,
    IInvitationDelivery delivery,
    IInvitationLinkBuilder linkBuilder,
    TimeProvider timeProvider)
{
    public async Task<UserResult<UserRowView>> HandleAsync(
        InviteUserCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return UserResult<UserRowView>.Invalid(GroupErrors(validation));
        }

        var access = AccessValidation.Resolve(command.RoleCode, command.IsAllBranches, command.BranchIds);
        var role = PermissionCatalog.Find(access.RoleCode)!;

        // The raw token lives only in this closure and the delivery call.
        var (rawToken, tokenHash) = InvitationTokens.Generate();
        var expiresAt = timeProvider.GetUtcNow().AddDays(command.ExpiresInDays ?? InviteUserCommandValidator.DefaultExpiresInDays);

        var request = new CreateInvitationRequest(
            command.OrganizationId,
            command.ActorUserId,
            command.ClientIp,
            EmailNormalizer.Normalize(command.Email),
            (command.FirstName ?? string.Empty).Trim(),
            (command.LastName ?? string.Empty).Trim(),
            access,
            command.LinkTeamProfile == true && role.HasTeamProfile,
            tokenHash,
            expiresAt);

        return await store.CreateInvitationAsync(
            request,
            (context, ct) => SendAsync(context, rawToken, ct),
            cancellationToken);
    }

    private Task SendAsync(InvitationDeliveryContext context, string rawToken, CancellationToken cancellationToken) =>
        delivery.SendAsync(
            new InvitationDeliveryMessage(
                context.RecipientEmail,
                context.FirstName,
                context.OrganizationName,
                context.InviterName,
                context.RoleName,
                context.ExpiresAt,
                linkBuilder.BuildAcceptLink(rawToken)),
            cancellationToken);

    internal static IReadOnlyDictionary<string, string[]> GroupErrors(
        FluentValidation.Results.ValidationResult validation) =>
        validation.Errors
            .GroupBy(error => error.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray(),
                StringComparer.Ordinal);
}

/// <summary>POST /users/invitations/{id}/resend (BR-11).</summary>
public sealed class ResendInvitationHandler(
    IUserAccessStore store,
    IInvitationDelivery delivery,
    IInvitationLinkBuilder linkBuilder,
    TimeProvider timeProvider)
{
    public const int ResendExpiryDays = 7;

    public Task<UserResult<UserRowView>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        var (rawToken, tokenHash) = InvitationTokens.Generate();
        var expiresAt = timeProvider.GetUtcNow().AddDays(ResendExpiryDays);

        return store.ResendInvitationAsync(
            organizationId,
            actorUserId,
            clientIp,
            invitationId,
            tokenHash,
            expiresAt,
            (context, ct) => delivery.SendAsync(
                new InvitationDeliveryMessage(
                    context.RecipientEmail,
                    context.FirstName,
                    context.OrganizationName,
                    context.InviterName,
                    context.RoleName,
                    context.ExpiresAt,
                    linkBuilder.BuildAcceptLink(rawToken)),
                ct),
            cancellationToken);
    }
}

public sealed class RevokeInvitationHandler(IUserAccessStore store, TimeProvider timeProvider)
{
    public Task<UserResult<NoValue>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid invitationId,
        CancellationToken cancellationToken) =>
        store.RevokeInvitationAsync(
            organizationId, actorUserId, clientIp, invitationId, timeProvider.GetUtcNow(), cancellationToken);
}

/// <summary>PUT /users/{id}/access (FR-10, BR-09, BR-15).</summary>
public sealed class UpdateMemberAccessHandler(
    IValidator<UpdateAccessCommand> validator,
    IUserAccessStore store,
    TimeProvider timeProvider)
{
    public async Task<UserResult<UserRowView>> HandleAsync(
        UpdateAccessCommand command, Guid actorMembershipId, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return UserResult<UserRowView>.Invalid(InviteUserHandler.GroupErrors(validation));
        }

        var access = AccessValidation.Resolve(command.RoleCode, command.IsAllBranches, command.BranchIds);

        return await store.UpdateMemberAccessAsync(
            command.OrganizationId,
            command.ActorUserId,
            actorMembershipId,
            command.ClientIp,
            command.TargetId,
            access,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}

/// <summary>PUT /users/invitations/{id}/access (FR-10, BR-09).</summary>
public sealed class UpdateInvitationAccessHandler(
    IValidator<UpdateAccessCommand> validator,
    IUserAccessStore store)
{
    public async Task<UserResult<UserRowView>> HandleAsync(
        UpdateAccessCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);

        if (!validation.IsValid)
        {
            return UserResult<UserRowView>.Invalid(InviteUserHandler.GroupErrors(validation));
        }

        var access = AccessValidation.Resolve(command.RoleCode, command.IsAllBranches, command.BranchIds);

        return await store.UpdateInvitationAccessAsync(
            command.OrganizationId, command.ActorUserId, command.ClientIp, command.TargetId, access, cancellationToken);
    }
}

public sealed class SuspendMemberHandler(IUserAccessStore store, TimeProvider timeProvider)
{
    public Task<UserResult<NoValue>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        Guid actorMembershipId,
        IPAddress? clientIp,
        Guid membershipId,
        CancellationToken cancellationToken) =>
        store.SuspendMemberAsync(
            organizationId,
            actorUserId,
            actorMembershipId,
            clientIp,
            membershipId,
            timeProvider.GetUtcNow(),
            cancellationToken);
}

public sealed class ReactivateMemberHandler(IUserAccessStore store, TimeProvider timeProvider)
{
    public Task<UserResult<NoValue>> HandleAsync(
        Guid organizationId,
        Guid actorUserId,
        IPAddress? clientIp,
        Guid membershipId,
        CancellationToken cancellationToken) =>
        store.ReactivateMemberAsync(
            organizationId, actorUserId, clientIp, membershipId, timeProvider.GetUtcNow(), cancellationToken);
}
