using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Customers;
using FieldOps.IntegrationTests.InvoicePayments;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;
using FieldOps.IntegrationTests.Users;

namespace FieldOps.IntegrationTests.BusinessOverview;

/// <summary>An organization with a deterministic business history and a foreign organization that must never leak in.</summary>
internal sealed record BusinessSeed(
    RequestWorld World, RequestWorld Foreign, string Owner, SeededMember OwnerMember, Guid InactiveBranch, Guid Drains);

internal static class BusinessOverviewSeed
{
    public const string Timezone = HubSeed.Timezone;

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(Timezone);

    public static string Window => $"from={HubSeed.Day(-29)}&to={HubSeed.Day(0)}";

    public static string Overview(string query = "", string? branch = null) =>
        $"/business-overview?{Window}{(branch is null ? string.Empty : $"&branchId={branch}")}{query}";

    public static decimal Dec(JsonNode? node) => node!.GetValue<decimal>();

    public static string Money(JsonNode? node) => Dec(node).ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTimeOffset Local(DateOnly day, int hour)
    {
        var local = day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);

        return new DateTimeOffset(local, Zone.GetUtcOffset(local)).ToUniversalTime();
    }

    // A local day without a DST change around it, so scheduled and available minutes stay exact.
    private static DateOnly PlainDay(int offset)
    {
        var day = HubSeed.Today().AddDays(offset);

        bool Plain(DateOnly candidate) =>
            Zone.GetUtcOffset(candidate.AddDays(-1).ToDateTime(TimeOnly.MinValue)) == Zone.GetUtcOffset(candidate.AddDays(1).ToDateTime(TimeOnly.MinValue));

        while (!Plain(day))
        {
            day = day.AddDays(offset >= 0 ? 1 : -1);
        }

        return day;
    }

    private static async Task<Guid[]> QuoteLinesAsync(CompanySettingsDatabaseFixture db, Guid order)
    {
        var lines = await db.QueryGuidsAsync(
            "SELECT ql.id FROM quote_lines ql JOIN work_orders wo ON wo.quote_version_id = ql.quote_version_id WHERE wo.id = @o ORDER BY ql.sort_order",
            ("o", order));

        return [.. lines];
    }

    private static Task SetCostAsync(CompanySettingsDatabaseFixture db, Guid quoteLine, decimal cost) =>
        db.ExecuteAsync("UPDATE quote_lines SET unit_cost = @c WHERE id = @l", ("c", cost), ("l", quoteLine));

    private static async Task<Guid> MaterialAsync(CompanySettingsDatabaseFixture db, Guid order, decimal cost)
    {
        var id = Guid.NewGuid();

        await db.ExecuteAsync(
            "INSERT INTO visit_materials (id, visit_id, description, quantity, unit, unit_cost) SELECT @id, id, 'Part', 1, 'ea', @c FROM visits WHERE work_order_id = @o",
            ("id", id),
            ("c", cost),
            ("o", order));

        return id;
    }

    private static Task LineAsync(CompanySettingsDatabaseFixture db, Guid invoice, decimal quantity, Guid? quoteLine, Guid? material) =>
        db.ExecuteAsync(
            """
            INSERT INTO invoice_lines (invoice_id, source_quote_line_id, source_visit_material_id, description, quantity, unit, unit_price,
                line_subtotal, line_tax, line_total)
            VALUES (@i, @q, @m, 'Line', @qty, 'ea', 1, 1, 0, 1)
            """,
            ("i", invoice),
            ("q", quoteLine),
            ("m", material),
            ("qty", quantity));

    public static async Task<BusinessSeed> SeedAsync(CompanySettingsDatabaseFixture db, RequestsHost host)
    {
        var world = await db.SeedWorldAsync(Timezone);
        var foreign = await db.SeedWorldAsync(Timezone);
        var (owner, member) = await host.SignInAsync(db, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Olive", "Owner");
        var user = member.UserId;
        var inactive = (await db.SeedBranchAsync(world.Org, "Gamma, Inc", isActive: false)).Id;
        var drains = await db.SeedCategoryAsync(world.Org, "=Drains");

        // Recognized, draft and void invoices of three branches (one inactive) in two service categories, plus one in the previous period.
        var i1 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -2, DueDays = 5, Total = 200m });
        var i2 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "paid", IssueDays = -5, DueDays = null, Total = 100m, Paid = 100m });
        var i3 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -29, DueDays = -10, Total = 300m, Branch = world.BranchB });
        var i4 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "draft", IssueDays = -1, DueDays = null, Total = 999m, Recipient = null });
        var iv = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { Status = "void", IssueDays = -2, DueDays = null, Total = 777m });
        var i6 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -29, DueDays = -29, Total = 50m, Branch = inactive });
        var i7 = await db.SeedInvoiceAsync(world, user, new InvoiceSpec { IssueDays = -40, DueDays = -35, Total = 100m });

        await db.ExecuteAsync("UPDATE invoices SET discount_total = 20 WHERE id = @i", ("i", i2.Id));
        await db.ExecuteAsync("UPDATE work_orders SET service_category_id = @c WHERE id = @o", ("c", drains), ("o", i2.Order));

        // Cost sources: a quote line, a visit material, a line with neither, and a line with both (the quote line wins).
        var q1 = await QuoteLinesAsync(db, i1.Order);
        await SetCostAsync(db, q1[0], 10m);
        await SetCostAsync(db, q1[1], 10m);
        await LineAsync(db, i1.Id, 2m, q1[0], null);
        await LineAsync(db, i1.Id, 1m, null, await MaterialAsync(db, i1.Order, 5m));
        await LineAsync(db, i1.Id, 3m, null, null);
        await LineAsync(db, i1.Id, 1m, q1[1], await MaterialAsync(db, i1.Order, 99m));
        var q2 = await QuoteLinesAsync(db, i2.Order);
        await SetCostAsync(db, q2[0], 10m);
        await LineAsync(db, i2.Id, 7m, q2[0], null);
        var q3 = await QuoteLinesAsync(db, i3.Order);
        await SetCostAsync(db, q3[0], 200m);
        await LineAsync(db, i3.Id, 1m, q3[0], null);
        var q4 = await QuoteLinesAsync(db, i4.Order);
        await SetCostAsync(db, q4[0], 500m);
        await LineAsync(db, i4.Id, 1m, q4[0], null);

        // Quotes sent in the period, one sent before it; every seeded job already carries an approved response of today.
        foreach (var order in new[] { i1.Order, i3.Order, i6.Order })
        {
            await db.ExecuteAsync(
                "UPDATE quote_versions SET sent_at = now() - interval '3 days' WHERE id = (SELECT quote_version_id FROM work_orders WHERE id = @o)",
                ("o", order));
        }

        await db.ExecuteAsync(
            "UPDATE quote_versions SET sent_at = now() - interval '45 days' WHERE id = (SELECT quote_version_id FROM work_orders WHERE id = @o)",
            ("o", i7.Order));
        await db.ExecuteAsync(
            "UPDATE visits SET actual_started_at = now() - interval '45 days', actual_completed_at = now() - interval '40 days' WHERE work_order_id = @o",
            ("o", i7.Order));

        // On-time arrivals: one on time (branch A), one 3 hours late (branch B).
        await db.ExecuteAsync(
            "UPDATE visits SET scheduled_start = actual_started_at - interval '10 minutes', scheduled_end = actual_completed_at WHERE work_order_id = @o",
            ("o", i1.Order));
        await db.ExecuteAsync(
            "UPDATE visits SET scheduled_start = actual_started_at - interval '3 hours', scheduled_end = actual_completed_at WHERE work_order_id = @o",
            ("o", i3.Order));

        foreach (var (invoice, rating) in new[] { (i1, 5), (i2, 4), (i3, 3) })
        {
            await db.ExecuteAsync(
                "INSERT INTO invoice_reviews (organization_id, work_order_id, invoice_id, rating) VALUES (@org, @o, @i, @r)",
                ("org", world.Org),
                ("o", invoice.Order),
                ("i", invoice.Id),
                ("r", (short)rating));
        }

        // Payments: a partially refunded one (net 75), one of branch B, one of the previous period.
        var refunded = await db.SeedPaymentAsync(world.Org, i2, 100m, -4, "cash", null, user, user);
        await db.ExecuteAsync(
            "UPDATE payments SET status = CAST('partially_refunded' AS payment_status), refunded_amount = 25 WHERE id = @p", ("p", refunded.Id));
        await db.SeedPaymentAsync(world.Org, i3, 40m, -2, "cash", null, user, user);
        await db.SeedPaymentAsync(world.Org, i7, 30m, -40, "cash", null, user, user);

        // Requests: two of branch A and one without branch in the period, one before it.
        await db.SeedRequestAsync(world, branch: world.BranchA, createdAt: DateTimeOffset.UtcNow.AddDays(-1));
        await db.SeedRequestAsync(world, branch: world.BranchA, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        await db.SeedRequestAsync(world, branch: null, createdAt: DateTimeOffset.UtcNow.AddDays(-3));
        await db.SeedRequestAsync(world, branch: world.BranchA, createdAt: DateTimeOffset.UtcNow.AddDays(-40));

        await SeedTeamAsync(db, world, user);

        // Another organization with data in every table the overview reads.
        var foreignUser = (await db.SeedMemberAsync(foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId, "Fay", "Foreign")).UserId;
        var foreignInvoice = await db.SeedInvoiceAsync(foreign, foreignUser, new InvoiceSpec { IssueDays = -2, DueDays = -2, Total = 5000m });
        await db.SeedPaymentAsync(foreign.Org, foreignInvoice, 5000m, -1, "cash", null, foreignUser, foreignUser);
        await db.SeedRequestAsync(foreign, branch: foreign.BranchA);

        return new BusinessSeed(world, foreign, owner, member, inactive, drains);
    }

    // Tina (active, 08:00-16:00 daily): 4 h, 11 h (180 min overtime) and 4 h tomorrow, plus 9 h in the previous period (60 min).
    // Ivan is inactive and must not count.
    private static async Task SeedTeamAsync(CompanySettingsDatabaseFixture db, RequestWorld world, Guid user)
    {
        var tina = await db.SeedTechAsync(world.Org, world.BranchA, "Tina", "One");
        var ivan = await db.SeedTechAsync(world.Org, world.BranchA, "Ivan", "Out", status: "inactive");

        foreach (var tech in new[] { tina, ivan })
        {
            for (short day = 0; day < 7; day++)
            {
                await db.SeedSlotAsync(tech, day, "08:00", "16:00");
            }
        }

        await db.SeedAssignmentAsync(world.Org, user, world.BranchA, tina, "completed", Local(PlainDay(-3), 9), Local(PlainDay(-3), 13));
        await db.SeedAssignmentAsync(world.Org, user, world.BranchA, tina, "completed", Local(PlainDay(-8), 7), Local(PlainDay(-8), 18));
        await db.SeedAssignmentAsync(world.Org, user, world.BranchA, tina, "completed", Local(PlainDay(-40), 8), Local(PlainDay(-40), 17));
        await db.SeedAssignmentAsync(world.Org, user, world.BranchA, tina, "scheduled", Local(PlainDay(1), 9), Local(PlainDay(1), 13));
        await db.SeedAssignmentAsync(world.Org, user, world.BranchA, ivan, "completed", Local(PlainDay(-5), 8), Local(PlainDay(-5), 16));
    }

    /// <summary>Parses RFC 4180 text into rows of cells.</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (quoted)
            {
                if (current == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (current == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(current);
                }
            }
            else if (current == '"')
            {
                quoted = true;
            }
            else if (current == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                rows.Add([.. cells]);
                cells.Clear();
                index++;
            }
            else
            {
                cell.Append(current);
            }
        }

        return rows;
    }
}
