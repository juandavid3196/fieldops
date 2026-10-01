namespace FieldOps.Domain.Technicians;

public sealed class Skill
{
    private Skill()
    {
    }

    private Skill(Guid id, Guid organizationId, string name)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public static Skill Create(Guid organizationId, string name, string? description = null)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Skill name is required.",
                nameof(name));
        }

        return new Skill(Guid.NewGuid(), organizationId, name.Trim())
        {
            Description = description,
        };
    }

    /// <summary>Replaces name and description; false when nothing changes.</summary>
    public bool Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name is required.", nameof(name));
        }

        var trimmed = name.Trim();

        if (Name == trimmed && Description == description)
        {
            return false;
        }

        Name = trimmed;
        Description = description;

        return true;
    }

    /// <summary>Activates or deactivates; false when the skill already has that state.</summary>
    public bool SetActive(bool active)
    {
        if (IsActive == active)
        {
            return false;
        }

        IsActive = active;

        return true;
    }
}
