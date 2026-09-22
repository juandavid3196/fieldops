namespace FieldOps.Domain.Users;

public sealed class Permission
{
    private Permission()
    {
    }

    private Permission(string code, string description)
    {
        Code = code;
        Description = description;
    }

    public short Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public static Permission Create(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Permission code is required.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Permission description is required.",
                nameof(description));
        }

        return new Permission(code.Trim(), description.Trim());
    }
}
