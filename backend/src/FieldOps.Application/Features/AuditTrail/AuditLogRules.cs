using System.Globalization;
using FieldOps.Application.Features.BillingReview;
using FieldOps.Application.Features.ServiceRequests;

namespace FieldOps.Application.Features.AuditTrail;

/// <summary>Pure parsing of the BR-03 to BR-05 query: a 400 key per bad parameter, then the typed query.</summary>
public static class AuditLogRules
{
    private static readonly int[] PageSizes = [25, 50, 100];

    public const string SystemActor = "system";

    public static BillingOutcome<AuditQuery> Parse(
        AuditLogQueryText text, string roleCode, DateOnly today, TimeZoneInfo zone, bool paged)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var from = Date(text.From, today, "from", errors);
        var to = Date(text.To, today, "to", errors);

        if (from is { } start && to is { } end)
        {
            if (start > end)
            {
                errors["from"] = ["From must be on or before To."];
            }
            else if (end.DayNumber - start.DayNumber + 1 > AuditLogMessages.MaxRangeDays)
            {
                errors["to"] = [$"The range can cover up to {AuditLogMessages.MaxRangeDays} days."];
            }
        }

        var search = text.Q?.Trim();

        if (search is { Length: > AuditLogMessages.SearchMaxLength })
        {
            errors["q"] = [$"Search must be {AuditLogMessages.SearchMaxLength} characters or fewer."];
        }

        Guid? actorId = null;
        var actorSystem = false;
        var actorText = text.ActorId?.Trim();

        if (!string.IsNullOrEmpty(actorText))
        {
            if (string.Equals(actorText, SystemActor, StringComparison.OrdinalIgnoreCase))
            {
                actorSystem = true;
            }
            else if (Guid.TryParse(actorText, out var parsedActor))
            {
                actorId = parsedActor;
            }
            else
            {
                errors["actorId"] = ["Actor is not valid."];
            }
        }

        var action = text.Action?.Trim();

        if (!string.IsNullOrEmpty(action) && !AuditAccess.CanSee(roleCode, action))
        {
            errors["action"] = ["Action is not valid."];
        }

        var entityType = text.EntityType?.Trim();

        if (!string.IsNullOrEmpty(entityType) && !AuditCatalog.EntityTypes.Contains(entityType, StringComparer.Ordinal))
        {
            errors["entityType"] = ["Entity type is not valid."];
        }

        Guid? entityId = null;
        var entityIdText = text.EntityId?.Trim();

        if (!string.IsNullOrEmpty(entityIdText))
        {
            if (string.IsNullOrEmpty(entityType) || !Guid.TryParse(entityIdText, out var parsedEntity))
            {
                errors["entityId"] = ["Entity id is not valid."];
            }
            else
            {
                entityId = parsedEntity;
            }
        }

        AuditImpact? impact = null;

        if (!string.IsNullOrWhiteSpace(text.Impact))
        {
            if (AuditCatalog.TryParseImpact(text.Impact, out var parsedImpact))
            {
                impact = parsedImpact;
            }
            else
            {
                errors["impact"] = ["Impact must be low, medium or high."];
            }
        }

        var sortByImpact = false;
        var sort = text.Sort?.Trim();

        if (!string.IsNullOrEmpty(sort))
        {
            if (string.Equals(sort, AuditLogMessages.SortImpact, StringComparison.OrdinalIgnoreCase))
            {
                sortByImpact = true;
            }
            else if (!string.Equals(sort, AuditLogMessages.SortOccurredAt, StringComparison.OrdinalIgnoreCase))
            {
                errors["sort"] = ["Sort must be occurredAt or impact."];
            }
        }

        var descending = true;
        var direction = text.Direction?.Trim();

        if (!string.IsNullOrEmpty(direction))
        {
            if (string.Equals(direction, "asc", StringComparison.OrdinalIgnoreCase))
            {
                descending = false;
            }
            else if (!string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase))
            {
                errors["direction"] = ["Direction must be asc or desc."];
            }
        }

        var page = 1;
        var pageSize = AuditLogMessages.DefaultPageSize;

        if (paged)
        {
            if (!string.IsNullOrWhiteSpace(text.Page)
                && (!int.TryParse(text.Page.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1))
            {
                errors["page"] = ["Page must be 1 or greater."];
            }

            if (!string.IsNullOrWhiteSpace(text.PageSize)
                && (!int.TryParse(text.PageSize.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
                    || !PageSizes.Contains(pageSize)))
            {
                errors["pageSize"] = ["Page size must be 25, 50 or 100."];
            }
        }

        if (errors.Count > 0)
        {
            return new BillingOutcome<AuditQuery>.Invalid(errors);
        }

        Guid? branchId = null;
        var branchText = text.BranchId?.Trim();

        if (!string.IsNullOrEmpty(branchText))
        {
            // A malformed id is as unknown as any other: one identical not found (BR-03).
            if (!Guid.TryParse(branchText, out var parsedBranch))
            {
                return new BillingOutcome<AuditQuery>.NotFound();
            }

            branchId = parsedBranch;
        }

        var fromDay = from!.Value;
        var toDay = to!.Value;

        return new BillingOutcome<AuditQuery>.Succeeded(new AuditQuery(
            fromDay,
            toDay,
            OrganizationTime.StartOfDayUtc(fromDay, zone),
            OrganizationTime.StartOfDayUtc(toDay.AddDays(1), zone),
            string.IsNullOrEmpty(search) ? null : search,
            actorId,
            actorSystem,
            string.IsNullOrEmpty(action) ? null : action,
            string.IsNullOrEmpty(entityType) ? null : entityType,
            entityId,
            branchId,
            impact,
            sortByImpact,
            descending,
            page,
            pageSize));
    }

    private static DateOnly? Date(string? value, DateOnly today, string key, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return today;
        }

        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        errors[key] = ["Enter a valid date (yyyy-MM-dd)."];

        return null;
    }
}
