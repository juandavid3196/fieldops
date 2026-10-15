using FieldOps.Application.Features.Notifications;

namespace FieldOps.UnitTests.Notifications;

public class NotificationCatalogTests
{
    // BR-02 and BR-04, written out independently of the catalog: every event code, its one type and its role set.
    [Theory]
    [InlineData("request.created", "request", "owner,dispatcher")]
    [InlineData("customer.message_sent", "message", "owner,dispatcher")]
    [InlineData("quote.approved", "quote", "owner,dispatcher")]
    [InlineData("quote.rejected", "quote", "owner,dispatcher")]
    [InlineData("quote.clarification_requested", "quote", "owner,dispatcher")]
    [InlineData("visit.assigned", "visit", "owner,operations_manager,dispatcher")]
    [InlineData("visit.on_the_way", "visit", "owner,operations_manager,dispatcher")]
    [InlineData("visit.started", "visit", "owner,operations_manager,dispatcher")]
    [InlineData("visit.paused", "visit", "owner,operations_manager,dispatcher")]
    [InlineData("job.completed", "job", "owner,operations_manager,dispatcher,accounting")]
    [InlineData("invoice.sent", "invoice", "owner,accounting")]
    [InlineData("invoice.bank_transfer_reported", "invoice", "owner,accounting")]
    [InlineData("payment.received", "payment", "owner,accounting")]
    public void Entry_EachEventCode_MapsToOneTypeAndTheBr04RoleSet(string code, string type, string roles)
    {
        var entry = NotificationCatalog.Get(code);

        Assert.Equal((type, roles), (entry.Type, string.Join(',', entry.Roles)));
        Assert.Contains(type, NotificationCatalog.Types);
        Assert.Contains(code, NotificationCatalog.CodesForType(type));
    }

    [Fact]
    public void Catalog_RoleFilterAndKindPolicies_AgreeWithTheRoleSetsAndExcludeTechnicians()
    {
        Assert.Equal(13, NotificationCatalog.Codes.Count);
        Assert.Empty(NotificationCatalog.CodesForRole("technician"));
        Assert.Empty(NotificationCatalog.CodesForRole("unknown"));
        Assert.Empty(NotificationCatalog.CodesForRole("viewer"));
        Assert.Equal(
            ["invoice.bank_transfer_reported", "invoice.sent", "job.completed", "payment.received"],
            NotificationCatalog.CodesForRole("accounting").Order());
        Assert.DoesNotContain("request.created", NotificationCatalog.CodesForRole("operations_manager"));

        // BR-12: accounting reads no job or visit target; the viewer reads them.
        Assert.False(NotificationCatalog.CanReadKind("accounting", NotificationEntityKinds.WorkOrder));
        Assert.False(NotificationCatalog.CanReadKind("accounting", NotificationEntityKinds.Visit));
        Assert.True(NotificationCatalog.CanReadKind("viewer", NotificationEntityKinds.WorkOrder));
        Assert.True(NotificationCatalog.CanReadKind("accounting", NotificationEntityKinds.Invoice));
        Assert.False(NotificationCatalog.CanReadKind("technician", NotificationEntityKinds.Request));
    }

}
