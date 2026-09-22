using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitAssignmentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var visitId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();
        var assignedByUserId = Guid.NewGuid();

        var assignment = VisitAssignment.Create(visitId, technicianId, assignedByUserId);

        Assert.NotEqual(Guid.Empty, assignment.Id);
        Assert.Equal(visitId, assignment.VisitId);
        Assert.Equal(technicianId, assignment.TechnicianId);
        Assert.Equal(assignedByUserId, assignment.AssignedByUserId);
        Assert.True(assignment.IsPrimary);
        Assert.Null(assignment.UnassignedAt);
    }

    [Fact]
    public void Create_WithEmptyTechnicianId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitAssignment.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));
    }
}
