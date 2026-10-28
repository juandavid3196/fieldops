using FieldOps.Application.Authentication;
using FieldOps.DemoSeeder;
using FieldOps.Domain.Quotes;
using FieldOps.Infrastructure;
using FieldOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Loads fictitious demo data into a database reserved for the demo. It is never part of the API start-up, sends no
// email and has no reset: running it again on a loaded database changes nothing.
//
//   dotnet run --project tools/FieldOps.DemoSeeder -c Release -- --confirm-demo-database
//
// Required environment: ConnectionStrings__FieldOpsDatabase, DemoSeed__EmailDomain, DemoSeed__Password (12+ chars).
// The connection string and the password are never printed.

const string ConfirmFlag = "--confirm-demo-database";
const string AllowOthersFlag = "--allow-other-organizations";

if (!args.Contains(ConfirmFlag, StringComparer.Ordinal))
{
    Console.Error.WriteLine($"Refusing to run without {ConfirmFlag}. This tool writes demo data to the configured database.");
    return 2;
}

var builder = Host.CreateApplicationBuilder([]);
var domain = builder.Configuration["DemoSeed:EmailDomain"]?.Trim().ToLowerInvariant();
var password = builder.Configuration["DemoSeed:Password"];

if (string.IsNullOrWhiteSpace(domain) || domain.Contains('@') || !domain.Contains('.'))
{
    Console.Error.WriteLine("DemoSeed__EmailDomain is required (a domain such as demo.example.com, without '@').");
    return 2;
}

if (password is null || password.Length < 12)
{
    Console.Error.WriteLine("DemoSeed__Password is required and must have at least 12 characters.");
    return 2;
}

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<FieldOpsDbContext>();
var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

if (pending.Count > 0)
{
    Console.Error.WriteLine($"The database has {pending.Count} pending migration(s). Apply them first (see docs/deployment/demo-deployment.md).");
    return 3;
}

if (await db.Organizations.AnyAsync(organization => organization.PublicSlug == DemoDataBuilder.PublicSlug))
{
    Console.WriteLine("Demo organization already exists. Nothing to do.");
    return 0;
}

if (!args.Contains(AllowOthersFlag, StringComparer.Ordinal) && await db.Organizations.AnyAsync())
{
    Console.Error.WriteLine($"The database already contains other organizations, so it is not a demo-only database. Use {AllowOthersFlag} only if that is intended.");
    return 4;
}

var roleIds = await db.Roles.AsNoTracking().ToDictionaryAsync(role => role.Code, role => role.Id);
var set = new DemoDataBuilder(hasher, roleIds).Build(domain, password, DateTimeOffset.UtcNow);

await using var transaction = await db.Database.BeginTransactionAsync();
db.AddRange(set.Entities);

// quotes.approved_version_id and quote_versions.quote_id reference each other: insert the quotes first without the
// approved version, then set it (same transaction).
var approved = set.Entities.OfType<Quote>().Where(quote => quote.ApprovedVersionId is not null)
    .Select(quote => (Quote: quote, VersionId: quote.ApprovedVersionId))
    .ToList();

foreach (var (quote, _) in approved)
{
    db.Entry(quote).Property(item => item.ApprovedVersionId).CurrentValue = null;
}

await db.SaveChangesAsync();

foreach (var (quote, versionId) in approved)
{
    db.Entry(quote).Property(item => item.ApprovedVersionId).CurrentValue = versionId;
}

await db.SaveChangesAsync();

await db.Organizations
    .Where(organization => organization.Id == set.Organization.Id)
    .ExecuteUpdateAsync(update => update
        .SetProperty(organization => organization.NextQuoteNumber, set.NextQuoteNumber)
        .SetProperty(organization => organization.NextWorkOrderNumber, set.NextWorkOrderNumber)
        .SetProperty(organization => organization.NextInvoiceNumber, set.NextInvoiceNumber)
        .SetProperty(organization => organization.NextPaymentNumber, set.NextPaymentNumber)
        .SetProperty(organization => organization.NextRequestNumber, set.NextRequestNumber));

await transaction.CommitAsync();

Console.WriteLine($"Loaded demo organization '{set.Organization.Name}' ({set.Entities.Count} rows). Accounts (password: DemoSeed__Password):");

foreach (var (email, role) in set.Accounts)
{
    Console.WriteLine($"  {role,-18} {email}");
}

return 0;
