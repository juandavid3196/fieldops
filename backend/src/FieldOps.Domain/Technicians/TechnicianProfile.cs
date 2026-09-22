namespace FieldOps.Domain.Technicians;

public sealed class TechnicianProfile
{
    private TechnicianProfile()
    {
    }

    private TechnicianProfile(
        Guid id,
        Guid organizationId,
        Guid branchId,
        string firstName,
        string lastName)
    {
        Id = id;
        OrganizationId = organizationId;
        BranchId = branchId;
        FirstName = firstName;
        LastName = lastName;
        Status = TechnicianStatus.Active;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid BranchId { get; private set; }

    public Guid? OrganizationUserId { get; private set; }

    public string? EmployeeCode { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public TechnicianStatus Status { get; private set; }

    public string? ColorHex { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static TechnicianProfile Create(
        Guid organizationId,
        Guid branchId,
        string firstName,
        string lastName)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization id is required.",
                nameof(organizationId));
        }

        if (branchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Branch id is required.",
                nameof(branchId));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException(
                "Technician first name is required.",
                nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException(
                "Technician last name is required.",
                nameof(lastName));
        }

        return new TechnicianProfile(
            Guid.NewGuid(),
            organizationId,
            branchId,
            firstName.Trim(),
            lastName.Trim());
    }
}
