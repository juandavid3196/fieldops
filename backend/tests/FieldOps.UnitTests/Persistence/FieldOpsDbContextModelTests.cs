using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Quotes;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
using FieldOps.Domain.WorkOrders;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.UnitTests.Persistence;

/// <summary>
/// Builds the EF Core model without opening a database connection, to catch
/// mapping mistakes (missing keys, invalid foreign keys, duplicate names)
/// as part of the standard test run.
/// </summary>
public class FieldOpsDbContextModelTests
{
    private static FieldOpsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FieldOpsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=fieldops_model_build;Username=postgres;Password=postgres",
                npgsql => npgsql
                    .MapEnum<UserStatus>("user_status")
                    .MapEnum<CustomerType>("customer_type")
                    .MapEnum<CatalogItemType>("catalog_item_type")
                    .MapEnum<RequestStatus>("request_status")
                    .MapEnum<AssessmentStatus>("assessment_status")
                    .MapEnum<MessageVisibility>("message_visibility")
                    .MapEnum<QuoteStatus>("quote_status")
                    .MapEnum<WorkOrderStatus>("work_order_status")
                    .MapEnum<VisitStatus>("visit_status"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new FieldOpsDbContext(options);
    }

    [Theory]
    [InlineData(typeof(Customer), "customers")]
    [InlineData(typeof(CustomerContact), "customer_contacts")]
    [InlineData(typeof(Property), "properties")]
    [InlineData(typeof(CustomerNote), "customer_notes")]
    [InlineData(typeof(ServiceCategory), "service_categories")]
    [InlineData(typeof(CatalogItem), "catalog_items")]
    [InlineData(typeof(Skill), "skills")]
    [InlineData(typeof(TechnicianProfile), "technician_profiles")]
    [InlineData(typeof(TechnicianSkill), "technician_skills")]
    [InlineData(typeof(TechnicianWeeklyAvailability), "technician_weekly_availability")]
    [InlineData(typeof(TechnicianBreak), "technician_breaks")]
    [InlineData(typeof(TechnicianException), "technician_exceptions")]
    [InlineData(typeof(ServiceRequest), "service_requests")]
    [InlineData(typeof(RequestAttachment), "request_attachments")]
    [InlineData(typeof(RequestMessage), "request_messages")]
    [InlineData(typeof(RequestStatusHistory), "request_status_history")]
    [InlineData(typeof(Assessment), "assessments")]
    [InlineData(typeof(AssessmentAttachment), "assessment_attachments")]
    [InlineData(typeof(Quote), "quotes")]
    [InlineData(typeof(QuoteVersion), "quote_versions")]
    [InlineData(typeof(QuoteLine), "quote_lines")]
    [InlineData(typeof(QuoteResponse), "quote_responses")]
    [InlineData(typeof(WorkOrder), "work_orders")]
    [InlineData(typeof(WorkOrderRequiredSkill), "work_order_required_skills")]
    [InlineData(typeof(WorkOrderChecklistTemplate), "work_order_checklist_templates")]
    [InlineData(typeof(Visit), "visits")]
    [InlineData(typeof(VisitAssignment), "visit_assignments")]
    [InlineData(typeof(VisitStatusHistory), "visit_status_history")]
    [InlineData(typeof(VisitTimeEntry), "visit_time_entries")]
    [InlineData(typeof(VisitChecklistItem), "visit_checklist_items")]
    [InlineData(typeof(VisitMaterial), "visit_materials")]
    [InlineData(typeof(VisitEvidence), "visit_evidence")]
    [InlineData(typeof(VisitIncident), "visit_incidents")]
    [InlineData(typeof(CustomerSignoff), "customer_signoffs")]
    public void Model_MapsEntityToExpectedTable(Type entityType, string expectedTableName)
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Equal(expectedTableName, entity!.GetTableName());
    }

    [Fact]
    public void Model_ConfiguresCompositeTenantForeignKeys()
    {
        using var context = CreateContext();

        var contact = context.Model.FindEntityType(typeof(CustomerContact));
        var contactToCustomerFk = Assert.Single(
            contact!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Customer));
        Assert.Equal(2, contactToCustomerFk.Properties.Count);

        var catalogItem = context.Model.FindEntityType(typeof(CatalogItem));
        var catalogItemToCategoryFk = Assert.Single(
            catalogItem!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(ServiceCategory));
        Assert.Equal(2, catalogItemToCategoryFk.Properties.Count);

        var technicianProfile = context.Model.FindEntityType(typeof(TechnicianProfile));
        var technicianToOrganizationUserFk = Assert.Single(
            technicianProfile!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(OrganizationUser));
        Assert.Equal(2, technicianToOrganizationUserFk.Properties.Count);
        Assert.False(technicianToOrganizationUserFk.IsRequired);
    }

    [Fact]
    public void Model_ConfiguresTechnicianSkillCompositeKeyAndCascadeDeletes()
    {
        using var context = CreateContext();

        var technicianSkill = context.Model.FindEntityType(typeof(TechnicianSkill));
        var primaryKeyProperties = technicianSkill!.FindPrimaryKey()!.Properties;

        Assert.Equal(2, primaryKeyProperties.Count);

        var toTechnicianFk = Assert.Single(
            technicianSkill.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(TechnicianProfile));
        Assert.Equal(DeleteBehavior.Cascade, toTechnicianFk.DeleteBehavior);

        var toSkillFk = Assert.Single(
            technicianSkill.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Skill));
        Assert.Equal(DeleteBehavior.NoAction, toSkillFk.DeleteBehavior);
    }

    [Theory]
    [InlineData(typeof(CustomerContact))]
    [InlineData(typeof(Property))]
    [InlineData(typeof(CustomerNote))]
    public void Model_ConfiguresDirectOrganizationForeignKey(Type entityType)
    {
        using var context = CreateContext();

        var entity = context.Model.FindEntityType(entityType);

        Assert.Single(
            entity!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Organization)
                && fk.Properties.Count == 1);
    }

    [Fact]
    public void Model_ConfiguresServiceRequestOptionalCompositeForeignKeys()
    {
        using var context = CreateContext();

        var serviceRequest = context.Model.FindEntityType(typeof(ServiceRequest));

        var toCustomerFk = Assert.Single(
            serviceRequest!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Customer));
        Assert.Equal(2, toCustomerFk.Properties.Count);
        Assert.False(toCustomerFk.IsRequired);

        var toPropertyFk = Assert.Single(
            serviceRequest.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Property));
        Assert.Equal(2, toPropertyFk.Properties.Count);
        Assert.False(toPropertyFk.IsRequired);
    }

    [Fact]
    public void Model_ConfiguresAssessmentAttachmentWithoutOrganizationIdAndCascadeDelete()
    {
        using var context = CreateContext();

        var attachment = context.Model.FindEntityType(typeof(AssessmentAttachment));

        Assert.Null(attachment!.FindProperty("OrganizationId"));

        var toAssessmentFk = Assert.Single(attachment.GetForeignKeys());
        Assert.Equal(typeof(Assessment), toAssessmentFk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, toAssessmentFk.DeleteBehavior);
    }

    [Fact]
    public void Model_ConfiguresQuoteAndQuoteVersionCircularRelationship()
    {
        using var context = CreateContext();

        var quote = context.Model.FindEntityType(typeof(Quote));
        var toApprovedVersionFk = Assert.Single(
            quote!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(QuoteVersion));
        Assert.Single(toApprovedVersionFk.Properties);
        Assert.False(toApprovedVersionFk.IsRequired);

        var quoteVersion = context.Model.FindEntityType(typeof(QuoteVersion));
        var toQuoteFk = Assert.Single(
            quoteVersion!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Quote));
        Assert.Equal(2, toQuoteFk.Properties.Count);
        Assert.True(toQuoteFk.IsRequired);

        var uniqueVersionNoIndex = Assert.Single(
            quoteVersion.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(["QuoteId", "VersionNo"]));
        Assert.NotNull(uniqueVersionNoIndex);
    }

    [Fact]
    public void Model_ConfiguresQuoteLineWithoutOrganizationIdAndCascadeDelete()
    {
        using var context = CreateContext();

        var quoteLine = context.Model.FindEntityType(typeof(QuoteLine));

        Assert.Null(quoteLine!.FindProperty("OrganizationId"));

        var toQuoteVersionFk = Assert.Single(
            quoteLine.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(QuoteVersion));
        Assert.Equal(DeleteBehavior.Cascade, toQuoteVersionFk.DeleteBehavior);

        var toCatalogItemFk = Assert.Single(
            quoteLine.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(CatalogItem));
        Assert.Equal(DeleteBehavior.NoAction, toCatalogItemFk.DeleteBehavior);
    }

    [Fact]
    public void Model_ConfiguresQuoteResponseWithoutOrganizationId()
    {
        using var context = CreateContext();

        var quoteResponse = context.Model.FindEntityType(typeof(QuoteResponse));

        Assert.Null(quoteResponse!.FindProperty("OrganizationId"));
        Assert.Equal(2, quoteResponse.GetForeignKeys().Count());
    }

    [Fact]
    public void Model_ConfiguresWorkOrderUniqueQuoteVersion()
    {
        using var context = CreateContext();

        var workOrder = context.Model.FindEntityType(typeof(WorkOrder));

        // quote_version_id uuid NOT NULL UNIQUE: enforces one WorkOrder per
        // approved QuoteVersion.
        var quoteVersionIndex = Assert.Single(
            workOrder!.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(["QuoteVersionId"]));
        Assert.NotNull(quoteVersionIndex);
    }

    [Fact]
    public void Model_ConfiguresVisitAssignmentPartialUniqueIndexAndCascade()
    {
        using var context = CreateContext();

        var visitAssignment = context.Model.FindEntityType(typeof(VisitAssignment));

        var activeUniqueIndex = Assert.Single(
            visitAssignment!.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(["VisitId", "TechnicianId"]));
        Assert.Equal("unassigned_at IS NULL", activeUniqueIndex.GetFilter());

        var toVisitFk = Assert.Single(
            visitAssignment.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Visit));
        Assert.Equal(DeleteBehavior.Cascade, toVisitFk.DeleteBehavior);
    }

    [Fact]
    public void Model_ConfiguresCustomerSignoffNoActionExceptionAndUniqueVisit()
    {
        using var context = CreateContext();

        var customerSignoff = context.Model.FindEntityType(typeof(CustomerSignoff));

        var toVisitFk = Assert.Single(
            customerSignoff!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Visit));
        Assert.Equal(DeleteBehavior.NoAction, toVisitFk.DeleteBehavior);

        var uniqueVisitIndex = Assert.Single(
            customerSignoff.GetIndexes(),
            index => index.IsUnique
                && index.Properties.Select(p => p.Name).SequenceEqual(["VisitId"]));
        Assert.NotNull(uniqueVisitIndex);
    }

    [Fact]
    public void Model_ConfiguresVisitChildTablesWithCascadeDelete()
    {
        using var context = CreateContext();

        Type[] visitChildEntityTypes =
        [
            typeof(VisitAssignment),
            typeof(VisitStatusHistory),
            typeof(VisitTimeEntry),
            typeof(VisitChecklistItem),
            typeof(VisitMaterial),
            typeof(VisitEvidence),
            typeof(VisitIncident),
        ];

        foreach (var entityType in visitChildEntityTypes)
        {
            var entity = context.Model.FindEntityType(entityType);
            var toVisitFk = Assert.Single(
                entity!.GetForeignKeys(),
                fk => fk.PrincipalEntityType.ClrType == typeof(Visit));
            Assert.Equal(DeleteBehavior.Cascade, toVisitFk.DeleteBehavior);
        }
    }
}
