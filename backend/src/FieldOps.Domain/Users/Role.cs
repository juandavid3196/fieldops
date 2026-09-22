namespace FieldOps.Domain.Users;

public sealed class Role
{
    private Role()
    {
    }

    private Role(string code, string name, string? description)
    {
        Code = code;
        Name = name;
        Description = description;
        IsCanonical = true;
    }

    public short Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsCanonical { get; private set; }

    public static Role Create(
        string code,
        string name,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Role code is required.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Role name is required.",
                nameof(name));
        }

        return new Role(code.Trim(), name.Trim(), description?.Trim());
    }
}
