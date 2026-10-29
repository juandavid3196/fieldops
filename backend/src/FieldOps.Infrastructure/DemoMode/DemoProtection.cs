using FieldOps.Application.Features.DemoMode;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FieldOps.Infrastructure.DemoMode;

/// <summary>
/// Resolves the configured slugs to organization ids once per minute (a demo loaded after start is picked up without a
/// restart) and answers membership questions from the database.
/// </summary>
internal sealed class DemoProtection(
    IOptions<DemoModeSettings> options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IDemoProtection
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    private readonly DemoModeSettings settings = options.Value;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private (DateTimeOffset ExpiresAt, HashSet<Guid> Ids) cache = (DateTimeOffset.MinValue, []);

    public bool Enabled => settings.Enabled;

    public bool IsProtectedSlug(string slug) =>
        Enabled && settings.OrganizationSlugs.Any(candidate => string.Equals(candidate.Trim(), slug.Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<bool> IsProtectedOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Enabled && (await OrganizationIdsAsync(cancellationToken)).Contains(organizationId);

    public async Task<bool> IsProtectedUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        var ids = await OrganizationIdsAsync(cancellationToken);

        if (ids.Count == 0)
        {
            return false;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();

        return await dbContext.OrganizationUsers.AsNoTracking()
                .AnyAsync(member => member.UserId == userId && ids.Contains(member.OrganizationId), cancellationToken)
            || await dbContext.CustomerContacts.AsNoTracking()
                .AnyAsync(contact => contact.PortalUserId == userId && ids.Contains(contact.OrganizationId), cancellationToken);
    }

    private async Task<HashSet<Guid>> OrganizationIdsAsync(CancellationToken cancellationToken)
    {
        var current = cache;

        if (current.ExpiresAt > timeProvider.GetUtcNow())
        {
            return current.Ids;
        }

        await refreshLock.WaitAsync(cancellationToken);

        try
        {
            current = cache;

            if (current.ExpiresAt > timeProvider.GetUtcNow())
            {
                return current.Ids;
            }

            var slugs = settings.OrganizationSlugs
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Select(slug => slug.Trim())
                .ToArray();

            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
            var ids = await dbContext.Organizations.AsNoTracking()
                .Where(organization => slugs.Contains(organization.PublicSlug))
                .Select(organization => organization.Id)
                .ToListAsync(cancellationToken);

            cache = (timeProvider.GetUtcNow() + CacheLifetime, ids.ToHashSet());

            return cache.Ids;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}
