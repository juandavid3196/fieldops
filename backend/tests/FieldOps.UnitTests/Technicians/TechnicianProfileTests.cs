using FieldOps.Domain.Technicians;

namespace FieldOps.UnitTests.Technicians;

public class TechnicianProfileTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var technician = TechnicianProfile.Create(organizationId, branchId, " Jane ", " Doe ");

        Assert.NotEqual(Guid.Empty, technician.Id);
        Assert.Equal(organizationId, technician.OrganizationId);
        Assert.Equal(branchId, technician.BranchId);
        Assert.Equal("Jane", technician.FirstName);
        Assert.Equal("Doe", technician.LastName);
        Assert.Equal(TechnicianStatus.Active, technician.Status);
        Assert.Null(technician.OrganizationUserId);
    }

    [Fact]
    public void Create_WithEmptyBranchId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianProfile.Create(Guid.NewGuid(), Guid.Empty, "Jane", "Doe"));
    }

    [Theory]
    [InlineData("", "Doe")]
    [InlineData("Jane", "")]
    public void Create_WithBlankName_Throws(string firstName, string lastName)
    {
        Assert.Throws<ArgumentException>(
            () => TechnicianProfile.Create(Guid.NewGuid(), Guid.NewGuid(), firstName, lastName));
    }
}
