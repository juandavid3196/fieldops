namespace FieldOps.Domain.Technicians;

public sealed class TechnicianSkill
{
    private TechnicianSkill()
    {
    }

    private TechnicianSkill(Guid technicianId, Guid skillId)
    {
        TechnicianId = technicianId;
        SkillId = skillId;
    }

    public Guid TechnicianId { get; private set; }

    public Guid SkillId { get; private set; }

    public short? Proficiency { get; private set; }

    public decimal? YearsExperience { get; private set; }

    public static TechnicianSkill Create(
        Guid technicianId,
        Guid skillId,
        short? proficiency = null,
        decimal? yearsExperience = null)
    {
        if (technicianId == Guid.Empty)
        {
            throw new ArgumentException(
                "Technician id is required.",
                nameof(technicianId));
        }

        if (skillId == Guid.Empty)
        {
            throw new ArgumentException(
                "Skill id is required.",
                nameof(skillId));
        }

        if (proficiency is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proficiency),
                proficiency,
                "Proficiency must be between 1 and 5.");
        }

        return new TechnicianSkill(technicianId, skillId)
        {
            Proficiency = proficiency,
            YearsExperience = yearsExperience,
        };
    }
}
