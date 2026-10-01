using FieldOps.Application.Features.Access;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

/// <summary>
/// Owner, operations manager, viewer and any all-branches member see every branch; other members see
/// the branches linked in <c>organization_user_branches</c>. A missing membership sees nothing (BR-02).
/// </summary>
internal sealed class BranchScopeResolver(FieldOpsDbContext dbContext) : IBranchScopeResolver
{
    private static readonly string[] AllBranchRoles = ["owner", "operations_manager", "viewer"];

    public async Task<BranchScope> ResolveAsync(
        Guid organizationId, Guid membershipId, CancellationToken cancellationToken)
    {
        var membership = await dbContext.OrganizationUsers.AsNoTracking()
            .Where(member => member.Id == membershipId && member.OrganizationId == organizationId)
            .Select(member => new
            {
                member.IsAllBranches,
                RoleCode = dbContext.Roles
                    .Where(role => role.Id == member.RoleId)
                    .Select(role => role.Code)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            return BranchScope.Nothing;
        }

        if (membership.IsAllBranches || (membership.RoleCode is not null && AllBranchRoles.Contains(membership.RoleCode)))
        {
            return BranchScope.Everything;
        }

        var branchIds = await dbContext.OrganizationUserBranches.AsNoTracking()
            .Where(link => link.OrganizationUserId == membershipId)
            .Join(
                dbContext.Branches.Where(branch => branch.OrganizationId == organizationId),
                link => link.BranchId,
                branch => branch.Id,
                (link, branch) => branch.Id)
            .ToListAsync(cancellationToken);

        return new BranchScope(false, branchIds);
    }
}
