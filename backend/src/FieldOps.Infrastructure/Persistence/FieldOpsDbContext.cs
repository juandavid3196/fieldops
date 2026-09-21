using FieldOps.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace FieldOps.Infrastructure.Persistence;

public sealed class FieldOpsDbContext(
    DbContextOptions<FieldOpsDbContext> options)
    : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(FieldOpsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
