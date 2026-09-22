using FieldOps.Domain.Notifications;

namespace FieldOps.UnitTests.Notifications;

public class AuditLogTests
{
    [Fact]
    public void Create_WithValidArguments_SetsDefaults()
    {
        var organizationId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var auditLog = AuditLog.Create(
            organizationId,
            " customer.updated ",
            " customer ",
            actorUserId,
            entityId,
            branchId);

        Assert.Equal(0, auditLog.Id);
        Assert.Equal(organizationId, auditLog.OrganizationId);
        Assert.Equal("customer.updated", auditLog.Action);
        Assert.Equal("customer", auditLog.EntityType);
        Assert.Equal(actorUserId, auditLog.ActorUserId);
        Assert.Equal(entityId, auditLog.EntityId);
        Assert.Equal(branchId, auditLog.BranchId);
        Assert.Equal("{}", auditLog.Metadata);
        Assert.Null(auditLog.BeforeData);
        Assert.Null(auditLog.AfterData);
        Assert.Null(auditLog.IpAddress);
    }

    [Fact]
    public void Create_WithoutOptionalValues_LeavesThemNull()
    {
        var auditLog = AuditLog.Create(Guid.NewGuid(), "action", "entity_type");

        Assert.Null(auditLog.ActorUserId);
        Assert.Null(auditLog.EntityId);
        Assert.Null(auditLog.BranchId);
    }

    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => AuditLog.Create(Guid.Empty, "action", "entity_type"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankAction_Throws(string action)
    {
        Assert.Throws<ArgumentException>(
            () => AuditLog.Create(Guid.NewGuid(), action, "entity_type"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEntityType_Throws(string entityType)
    {
        Assert.Throws<ArgumentException>(
            () => AuditLog.Create(Guid.NewGuid(), "action", entityType));
    }
}
