namespace FieldOps.Domain.Users;

public sealed class User
{
    private User()
    {
    }

    private User(
        Guid id,
        string email,
        string passwordHash,
        string firstName,
        string lastName)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        Status = UserStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTimeOffset? EmailVerifiedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static User Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "User email is required.",
                nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException(
                "User password hash is required.",
                nameof(passwordHash));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "User first name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "User last name is required.",
                nameof(lastName));
        }

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            passwordHash,
            firstName.Trim(),
            lastName.Trim());
    }

    /// <summary>
    /// Records a successful sign-in. Only active users can sign in.
    /// </summary>
    public void RecordSignIn(DateTimeOffset signedInAt)
    {
        if (signedInAt == default)
        {
            throw new ArgumentException(
                "Sign-in time is required.",
                nameof(signedInAt));
        }

        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException(
                "Only active users can sign in.");
        }

        LastLoginAt = signedInAt;
        UpdatedAt = signedInAt;
    }
}
