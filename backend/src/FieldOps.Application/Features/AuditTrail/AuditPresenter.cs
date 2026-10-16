using System.Globalization;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>
/// Turns a stored, resolved event into the redacted list item (BR-06), the detail (BR-13) and the CSV (BR-14). Every value
/// leaves through <see cref="AuditRedactor"/>.
/// </summary>
internal static class AuditPresenter
{
    private static readonly string[] CsvHeader =
    [
        "Event ID", "Timestamp (local)", "Timestamp (UTC)", "Time zone", "User", "Action", "Entity", "Reference", "Summary",
        "Branch", "Source", "IP address", "Impact",
    ];

    public const string AllBranches = "All branches";

    public static AuditItem Item(AuditResolved row)
    {
        var record = row.Record;
        var entry = AuditCatalog.Resolve(record.Action);
        var reference = Reference(row);

        return new AuditItem(
            record.Id,
            EventId(record.Id),
            record.OccurredAt,
            Actor(row),
            new AuditActionRef(record.Action, entry.Label),
            Entity(record),
            reference,
            Summary(row, entry, reference),
            Branch(row),
            Source(row),
            record.IpAddress,
            AuditCatalog.ImpactCode(entry.Impact));
    }

    public static AuditDetail Detail(AuditResolvedDetail detail, string timeZone)
    {
        var row = detail.Row;
        var record = row.Record;
        var entry = AuditCatalog.Resolve(record.Action);
        var reference = Reference(row);
        var before = AuditRedactor.Redact(record.BeforeData);
        var after = AuditRedactor.Redact(record.AfterData);
        var item = Actor(row);
        var source = Source(row);
        var metadata = new List<AuditMetadataPair>();
        var technical = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eventId"] = EventId(record.Id),
            ["entityType"] = record.EntityType,
            ["entityId"] = record.EntityId?.ToString() ?? AuditSummaryBuilder.Dash,
            ["actorUserId"] = record.ActorUserId?.ToString() ?? AuditSummaryBuilder.Dash,
            ["branchId"] = record.BranchId?.ToString() ?? AuditSummaryBuilder.Dash,
            ["source"] = source,
        };

        if (AuditRedactor.Redact(record.Metadata) is JsonObject stored)
        {
            metadata.AddRange(stored
                .Where(property => !technical.ContainsKey(property.Key))
                .Select(property => new AuditMetadataPair(property.Key, AuditSummaryBuilder.Display(property.Value))));
        }

        metadata.AddRange(technical.Select(pair => new AuditMetadataPair(pair.Key, pair.Value)));

        return new AuditDetail(
            record.Id,
            EventId(record.Id),
            record.OccurredAt,
            new AuditDetailActor(item.Id, item.Name, detail.ActorRoleName),
            new AuditActionRef(record.Action, entry.Label),
            Entity(record),
            reference,
            AuditSummaryBuilder.Build(entry.Label, reference, before, after),
            Branch(row),
            source,
            record.IpAddress,
            AuditCatalog.ImpactCode(entry.Impact),
            AuditSummaryBuilder.Changes(before, after),
            metadata,
            detail.Target,
            timeZone);
    }

    /// <summary>The BR-14 document: exact headers, local and UTC timestamps, no before/after values or metadata.</summary>
    public static byte[] Csv(IReadOnlyList<AuditItem> items, string timeZone)
    {
        var zone = OrganizationTime.FindZone(timeZone);
        var rows = new List<string> { CsvCell.Row(CsvHeader) };

        foreach (var item in items)
        {
            rows.Add(CsvCell.Row(
            [
                item.EventId,
                OrganizationTime.ToZone(item.OccurredAt, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                item.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                timeZone,
                item.Actor.Name,
                item.Action.Label,
                item.Entity.Label,
                item.Reference,
                item.Summary,
                item.Branch?.Name ?? AllBranches,
                item.Source,
                item.IpAddress ?? string.Empty,
                char.ToUpperInvariant(item.Impact[0]) + item.Impact[1..],
            ]));
        }

        return CsvCell.Document(rows);
    }

    public static string EventId(long id) => string.Create(CultureInfo.InvariantCulture, $"AUD-{id}");

    private static string Reference(AuditResolved row) =>
        string.IsNullOrWhiteSpace(row.Reference) ? AuditSummaryBuilder.Dash : row.Reference;

    private static string Summary(AuditResolved row, AuditCatalogEntry entry, string reference) =>
        AuditSummaryBuilder.Build(
            entry.Label, reference, AuditRedactor.Redact(row.Record.BeforeData), AuditRedactor.Redact(row.Record.AfterData));

    private static AuditActorRef Actor(AuditResolved row) =>
        row.Record.ActorUserId is { } id
            ? new AuditActorRef(id, string.IsNullOrWhiteSpace(row.ActorName) ? AuditSummaryBuilder.Dash : row.ActorName)
            : new AuditActorRef(null, AuditSourceResolver.AnonymousActor(row.Record.Action));

    private static AuditEntityRef Entity(AuditRecord record) =>
        new(record.EntityType, AuditCatalog.EntityLabel(record.EntityType), record.EntityId);

    private static AuditBranchRef? Branch(AuditResolved row) =>
        row.Record.BranchId is { } id ? new AuditBranchRef(id, row.BranchName ?? AuditSummaryBuilder.Dash) : null;

    private static string Source(AuditResolved row) =>
        AuditSourceResolver.Resolve(row.Record.ActorUserId, row.ActorIsMember, row.Record.Action);
}
