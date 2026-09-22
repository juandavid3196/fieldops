namespace FieldOps.Domain.Organizations;

public sealed class InvitationBranch
{
    private InvitationBranch()
    {
    }

    private InvitationBranch(Guid invitationId, Guid branchId)
    {
        InvitationId = invitationId;
        BranchId = branchId;
    }

    public Guid InvitationId { get; private set; }

    public Guid BranchId { get; private set; }

    public static InvitationBranch Create(Guid invitationId, Guid branchId)
    {
        if (invitationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Invitation id is required.",
                nameof(invitationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        return new InvitationBranch(invitationId, branchId);
    }
}
