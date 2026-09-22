using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class VisitIncidentTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var visitId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();

        var incident = VisitIncident.Create(
            visitId,
            " Water leak ",
            " Found a leak under the sink ",
            createdByUserId);

        Assert.Equal(visitId, incident.VisitId);
        Assert.Equal("Water leak", incident.Type);
        Assert.Equal("Found a leak under the sink", incident.Description);
        Assert.False(incident.AdditionalWorkRequested);
    }

    [Theory]
    [InlineData("", "Description")]
    [InlineData("Type", "")]
    public void Create_WithBlankRequiredField_Throws(string type, string description)
    {
        Assert.Throws<ArgumentException>(
            () => VisitIncident.Create(Guid.NewGuid(), type, description, Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyCreatedByUserId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => VisitIncident.Create(Guid.NewGuid(), "Type", "Description", Guid.Empty));
    }
}
