using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
using FieldOps.Domain.Organizations;
using FieldOps.Domain.Requests;
using FieldOps.Domain.Technicians;
using FieldOps.Domain.Users;
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
                    .MapEnum<MessageVisibility>("message_visibility"))
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
}
