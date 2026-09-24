using FieldOps.Domain.Users;

namespace FieldOps.UnitTests.Authentication;

internal static class TestUsers
{
    /// <summary>
    /// Creates a user with the given status. The domain has no status
    /// transition yet, so the private setter is used through reflection.
    /// </summary>
    public static User Create(
        UserStatus status,
        string email = "user@example.com",
        string passwordHash = "stored-hash")
    {
        var user = User.Create(email, passwordHash, "Ada", "Lovelace");

        typeof(User).GetProperty(nameof(User.Status))!.SetValue(user, status);

        return user;
    }
}
