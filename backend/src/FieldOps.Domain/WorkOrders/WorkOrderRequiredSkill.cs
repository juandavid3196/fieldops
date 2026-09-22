namespace FieldOps.Domain.WorkOrders;

public sealed class WorkOrderRequiredSkill
{
    private WorkOrderRequiredSkill()
    {
    }

    private WorkOrderRequiredSkill(Guid workOrderId, Guid skillId)
    {
        WorkOrderId = workOrderId;
        SkillId = skillId;
    }

    public Guid WorkOrderId { get; private set; }

    public Guid SkillId { get; private set; }

    public short? MinimumProficiency { get; private set; }

    public static WorkOrderRequiredSkill Create(
        Guid workOrderId,
        Guid skillId,
        short? minimumProficiency = null)
    {
        if (workOrderId == Guid.Empty)
        {
            throw new ArgumentException(
                "Work order id is required.",
                nameof(workOrderId));
        }

        if (skillId == Guid.Empty)
        {
            throw new ArgumentException(
                "Skill id is required.",
                nameof(skillId));
        }

        if (minimumProficiency is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumProficiency),
                minimumProficiency,
                "Minimum proficiency must be between 1 and 5.");
        }

        return new WorkOrderRequiredSkill(workOrderId, skillId)
        {
            MinimumProficiency = minimumProficiency,
        };
    }
}
