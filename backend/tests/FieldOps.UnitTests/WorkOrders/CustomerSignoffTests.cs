using FieldOps.Domain.WorkOrders;

namespace FieldOps.UnitTests.WorkOrders;

public class CustomerSignoffTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData(AcknowledgementMethods.Signed, true, true)]
    [InlineData(AcknowledgementMethods.RemoteConfirmation, false, true)]
    [InlineData(AcknowledgementMethods.CustomerAbsent, false, false)]
    [InlineData(AcknowledgementMethods.CustomerRefused, false, false)]
    public void Create_PerMethod_MapsAcceptedAndSignature(string method, bool withSignature, bool accepted)
    {
        var visitId = Guid.NewGuid();
        var user = Guid.NewGuid();

        var signoff = CustomerSignoff.Create(
            visitId, method, user, DateTimeOffset.UtcNow, signatureContent: withSignature ? Png : null);

        Assert.NotEqual(Guid.Empty, signoff.Id);
        Assert.Equal(visitId, signoff.VisitId);
        Assert.Equal(user, signoff.RecordedByUserId);
        Assert.Equal(accepted, signoff.Accepted);
        Assert.Equal(withSignature ? "image/png" : null, signoff.SignatureMimeType);
        Assert.Null(signoff.SignerContactId);
        Assert.Null(signoff.SignatureStorageKey);
    }

    [Theory]
    [InlineData(AcknowledgementMethods.Signed, false, "Only a signed")]
    [InlineData(AcknowledgementMethods.CustomerAbsent, true, "Only a signed")]
    public void Create_WithSignatureNotMatchingTheMethod_Throws(string method, bool withSignature, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => CustomerSignoff.Create(
            Guid.NewGuid(), method, Guid.NewGuid(), DateTimeOffset.UtcNow, signatureContent: withSignature ? Png : null));

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithEmptyVisitId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CustomerSignoff.Create(Guid.Empty, AcknowledgementMethods.CustomerAbsent, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
