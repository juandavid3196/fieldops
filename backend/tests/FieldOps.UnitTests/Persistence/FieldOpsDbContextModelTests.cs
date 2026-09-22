using FieldOps.Domain.Catalog;
using FieldOps.Domain.Customers;
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
                    .MapEnum<CatalogItemType>("catalog_item_type"))
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
    }
}
