namespace FieldOps.Domain.Organizations;

public sealed class OrganizationUserBranch
{
    private OrganizationUserBranch()
    {
    }

    private OrganizationUserBranch(Guid organizationUserId, Guid branchId)
    {
        OrganizationUserId = organizationUserId;
        BranchId = branchId;
    }

    public Guid OrganizationUserId { get; private set; }

    public Guid BranchId { get; private set; }

    public static OrganizationUserBranch Create(
        Guid organizationUserId,
        Guid branchId)
    {
        if (organizationUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization user id is required.",
                nameof(organizationUserId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        return new OrganizationUserBranch(organizationUserId, branchId);
    }
}
