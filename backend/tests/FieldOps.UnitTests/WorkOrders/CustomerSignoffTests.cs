using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class CustomerSignoffTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var visitId = Guid.NewGuid();

        var signoff = CustomerSignoff.Create(visitId, accepted: true);

        Assert.NotEqual(Guid.Empty, signoff.Id);
        Assert.Equal(visitId, signoff.VisitId);
        Assert.True(signoff.Accepted);
        Assert.Null(signoff.SignerName);
        Assert.Null(signoff.SignatureStorageKey);
        Assert.Null(signoff.AbsenceReason);
    }

    [Fact]
    public void Create_WithAcceptedFalse_PreservesExplicitFalse()
    {
        var signoff = CustomerSignoff.Create(Guid.NewGuid(), accepted: false);

        Assert.False(signoff.Accepted);
    }

    [Fact]
    public void Create_WithEmptyVisitId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CustomerSignoff.Create(Guid.Empty, accepted: true));
    }
}
