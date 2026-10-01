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
        string lastName,
        string? email = null,
        string? phone = null,
        string? employeeCode = null,
        string? notes = null)
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
            lastName.Trim())
        {
            Email = email,
            Phone = phone,
            EmployeeCode = employeeCode,
            Notes = notes,
        };
    }

    /// <summary>Replaces the editable profile fields; status, link, skills and availability are untouched.</summary>
    public void UpdateDetails(
        Guid branchId,
        string firstName,
        string lastName,
        string? email,
        string? phone,
        string? employeeCode,
        string? notes,
        DateTimeOffset now)
    {
        if (branchId == Guid.Empty)
        {
            throw new ArgumentException("Branch id is required.", nameof(branchId));
        }

        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new ArgumentException("Technician first name is required.", nameof(firstName));
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new ArgumentException("Technician last name is required.", nameof(lastName));
        }

        BranchId = branchId;
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = email;
        Phone = phone;
        EmployeeCode = employeeCode;
        Notes = notes;
        UpdatedAt = now;
    }

    /// <summary>Advances the profile version after weekly availability or skills changed.</summary>
    public void Touch(DateTimeOffset now) => UpdatedAt = now;

    /// <summary>Changes the status; false when it already has that status.</summary>
    public bool ChangeStatus(TechnicianStatus status, DateTimeOffset now)
    {
        if (Status == status)
        {
            return false;
        }

        Status = status;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Clears the membership link; false when the profile was not linked.</summary>
    public bool UnlinkMembership(DateTimeOffset now)
    {
        if (OrganizationUserId is null)
        {
            return false;
        }

        OrganizationUserId = null;
        UpdatedAt = now;

        return true;
    }

    /// <summary>Links an unlinked profile to a membership of its own organization.</summary>
    public void LinkMembership(Guid organizationUserId, DateTimeOffset now)
    {
        if (organizationUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Organization user id is required.",
                nameof(organizationUserId));
        }

        if (OrganizationUserId is not null)
        {
            throw new InvalidOperationException("The technician profile is already linked.");
        }

        OrganizationUserId = organizationUserId;
        UpdatedAt = now;
    }
}
