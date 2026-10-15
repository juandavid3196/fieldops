using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>A quote seeded for the quotes workspace: its request, sent (or draft) version, optional revision draft and work order.</summary>
internal sealed record WorkspaceQuote(Guid Id, Guid Request, long Number, Guid Version, Guid? Mutable, Guid? WorkOrder);

internal static class QuotesWorkspaceSeed
{
    /// <summary>The seeded world with quote prefix Q, request prefix REQ and the requested organization time zone.</summary>
    public static async Task<RequestWorld> SeedWorkspaceWorldAsync(
        this CompanySettingsDatabaseFixture db, string timezone = "UTC")
    {
        var world = await db.SeedWorldAsync(timezone);
        await db.ExecuteAsync(
            "UPDATE organizations SET quote_prefix = 'Q', request_prefix = 'REQ', currency = 'USD' WHERE id = @o", ("o", world.Org));

        return world;
    }

    /// <summary>
    /// Inserts a request and its quote directly. <c>currentVersion</c> 0 seeds a never-sent draft whose only version is
    /// mutable; otherwise an immutable sent version, plus a mutable revision draft when <c>revision</c> is set.
    /// <c>status</c> and the other text arguments are test-controlled constants, never user input.
    /// </summary>
    public static async Task<WorkspaceQuote> SeedWorkspaceQuoteAsync(
        this CompanySettingsDatabaseFixture db,
        RequestWorld world,
        Guid staff,
        string status,
        int currentVersion = 1,
        DateOnly? validUntil = null,
        decimal total = 100m,
        string currency = "USD",
        Guid? requestBranch = null,
        bool noRequestBranch = false,
        Guid? quoteBranch = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        bool revision = false,
        string? response = null,
        DateTimeOffset? respondedAt = null,
        decimal? responseTotal = null,
        string? workOrderStatus = null,
        bool linkCustomer = true,
        string? guestName = "Guest Person",
        string requestStatus = "quoted",
        bool withService = true,
        string description = "Leaking pipe under the sink")
    {
        var request = await db.SeedRequestAsync(
            world,
            status: requestStatus,
            branch: noRequestBranch ? null : requestBranch ?? world.BranchA,
            linkCustomer: linkCustomer,
            guestName: guestName,
            withService: withService,
            description: description);
        var quote = Guid.NewGuid();
        var version = Guid.NewGuid();
        var mutable = currentVersion == 0 || revision ? Guid.NewGuid() : (Guid?)null;
        var number = await db.ScalarAsync<long>(
            "SELECT COALESCE(MAX(quote_number), 0) + 1 FROM quotes WHERE organization_id = @o", ("o", world.Org));
        var created = (createdAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var updated = (updatedAt ?? created).ToUniversalTime();
        Guid? workOrder = workOrderStatus is null ? null : Guid.NewGuid();

        var sql = $"""
            INSERT INTO quotes (id, organization_id, branch_id, request_id, customer_id, property_id, quote_number, status, current_version_no, created_by_user_id, created_at, updated_at)
            VALUES (@quote, @org, @quoteBranch, @request, @customer, @property, @number, CAST('{status}' AS quote_status), @current, @user, @created, @updated);
            """;

        if (currentVersion == 0)
        {
            sql += """

                INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, valid_until, is_immutable, created_by_user_id)
                VALUES (@mutable, @org, @quote, 1, 'Scope', @total, 0, 0, @total, @currency, @valid, false, @user);
                """;
        }
        else
        {
            sql += """

                INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, valid_until, sent_at, is_immutable, created_by_user_id)
                VALUES (@version, @org, @quote, @current, 'Scope', @total, 0, 0, @total, @currency, @valid, @created, true, @user);
                """;
        }

        if (currentVersion > 0 && revision)
        {
            sql += """

                INSERT INTO quote_versions (id, organization_id, quote_id, version_no, scope, subtotal, discount_total, tax_total, total, currency, valid_until, is_immutable, created_by_user_id)
                VALUES (@mutable, @org, @quote, @nextVersion, 'Scope', 9999, 0, 0, 9999, @currency, NULL, false, @user);
                """;
        }

        if (response == "approved")
        {
            sql += """

                INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, responded_at, subtotal, discount_total, tax_total, total)
                VALUES (gen_random_uuid(), @org, @version, 'approved', 'Carla Customer', @responded, @responseTotal, 0, 0, @responseTotal);
                UPDATE quotes SET approved_version_id = @version WHERE id = @quote;
                """;
        }
        else if (response == "rejected")
        {
            sql += """

                INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, responded_at)
                VALUES (gen_random_uuid(), @org, @version, 'rejected', 'Carla Customer', @responded);
                """;
        }

        if (workOrder is not null)
        {
            sql += $"""

                INSERT INTO work_orders (id, organization_id, branch_id, work_order_number, quote_version_id, customer_id, property_id, title, service_category_id, status, scope_snapshot, created_by_user_id)
                VALUES (@order, @org, @requestBranchOrA, @number, @version, @customer, @property, 'Job', @category, CAST('{workOrderStatus}' AS work_order_status), 'Scope', @user);
                """;
        }

        await db.ExecuteAsync(
            sql,
            ("quote", quote),
            ("org", world.Org),
            ("quoteBranch", quoteBranch),
            ("request", request.Id),
            ("customer", linkCustomer ? world.Customer : null),
            ("property", linkCustomer ? world.Property : null),
            ("number", number),
            ("current", currentVersion),
            ("user", staff),
            ("created", created),
            ("updated", updated),
            ("version", version),
            ("mutable", mutable),
            ("nextVersion", currentVersion + 1),
            ("total", total),
            ("currency", currency),
            ("valid", validUntil),
            ("responded", (respondedAt ?? created.AddHours(2)).ToUniversalTime()),
            ("responseTotal", responseTotal ?? total),
            ("order", workOrder),
            ("requestBranchOrA", requestBranch ?? world.BranchA),
            ("category", world.Category));

        return new WorkspaceQuote(quote, request.Id, number, version, mutable, workOrder);
    }
}
