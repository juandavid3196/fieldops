namespace FieldOps.Domain.Users;

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    private RolePermission(short roleId, short permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public short RoleId { get; private set; }

    public short PermissionId { get; private set; }

    public static RolePermission Create(short roleId, short permissionId)
    {
        if (roleId <= 0)
        {
            throw new ArgumentException(
                "Role id is required.",
                nameof(roleId));
        }

        if (permissionId <= 0)
        {
            throw new ArgumentException(
                "Permission id is required.",
                nameof(permissionId));
        }

        return new RolePermission(roleId, permissionId);
    }
}
