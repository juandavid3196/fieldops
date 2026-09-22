using FieldOps.Domain.Customers;

namespace FieldOps.UnitTests.Customers;

public class CustomerNoteTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var authorUserId = Guid.NewGuid();

        var note = CustomerNote.Create(organizationId, customerId, authorUserId, " Called back. ");

        Assert.NotEqual(Guid.Empty, note.Id);
        Assert.Equal(organizationId, note.OrganizationId);
        Assert.Equal(customerId, note.CustomerId);
        Assert.Equal(authorUserId, note.AuthorUserId);
        Assert.Equal("Called back.", note.Note);
    }

    [Fact]
    public void Create_WithEmptyAuthorUserId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CustomerNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "Note"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankNote_Throws(string note)
    {
        Assert.Throws<ArgumentException>(
            () => CustomerNote.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), note));
    }
}
