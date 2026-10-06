using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using Npgsql;

namespace FieldOps.IntegrationTests.QuoteLinks;

/// <summary>The BR-25 schema amendments reject invalid rows (AC-22).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteLinkSchemaTests(CompanySettingsDatabaseFixture database)
{
    private const string Insert =
        """
        INSERT INTO quote_responses (organization_id, quote_version_id, response, responder_name, responder_contact_id, subtotal, discount_total, tax_total, total)
        VALUES (@o, @v, CAST('{0}' AS quote_status), 'Responder', @c, @s, @d, @t, @total)
        """;

    private async Task<string> RejectedByAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() => database.ExecuteAsync(sql, parameters));

        return $"{failure.SqlState}:{failure.ConstraintName}";
    }

    [Fact]
    public async Task Database_RejectsEveryRowThatBreaksTheQuoteResponseConstraints()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var first = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var second = await QuoteLinkApi.SendAsync(database, host, world, owner);
        var other = await QuoteLinkApi.SendAsync(database, host, foreign, foreignOwner);

        (string, object?)[] Row(Guid org, Guid version, object? contact = null, decimal? subtotal = null, decimal? discount = null, decimal? tax = null, decimal? total = null) =>
            [("o", org), ("v", version), ("c", contact), ("s", subtotal), ("d", discount), ("t", tax), ("total", total)];

        var approvedTotals = (190m, 10m, 14.85m, 194.85m);

        // Totals exist exactly on an approval (ck_quote_responses_totals, ck_quote_responses_totals_null) and are not negative.
        Assert.Equal("23514:ck_quote_responses_totals", await RejectedByAsync(string.Format(Insert, "approved"), Row(world.Org, first.VersionId)));
        Assert.Equal(
            "23514:ck_quote_responses_totals",
            await RejectedByAsync(string.Format(Insert, "approved"), Row(world.Org, first.VersionId, null, 1m, 0m, 0m, null)));
        Assert.StartsWith(
            "23514:ck_quote_responses_totals",
            await RejectedByAsync(string.Format(Insert, "rejected"), Row(world.Org, first.VersionId, null, 190m, 10m, 14.85m, 194.85m)));
        Assert.StartsWith(
            "23514:ck_quote_responses_totals",
            await RejectedByAsync(string.Format(Insert, "clarification_requested"), Row(world.Org, first.VersionId, null, 1m, null, null, null)));
        Assert.Equal(
            "23514:ck_quote_responses_total",
            await RejectedByAsync(string.Format(Insert, "approved"), Row(world.Org, first.VersionId, null, 190m, 10m, 14.85m, -1m)));

        // One final and one clarification per version (partial unique indexes).
        await database.ExecuteAsync(string.Format(Insert, "rejected"), Row(world.Org, first.VersionId));
        await database.ExecuteAsync(string.Format(Insert, "clarification_requested"), Row(world.Org, first.VersionId));
        Assert.Equal(
            "23505:ux_quote_responses_final",
            await RejectedByAsync(
                string.Format(Insert, "approved"),
                Row(world.Org, first.VersionId, null, approvedTotals.Item1, approvedTotals.Item2, approvedTotals.Item3, approvedTotals.Item4)));
        Assert.Equal("23505:ux_quote_responses_final", await RejectedByAsync(string.Format(Insert, "rejected"), Row(world.Org, first.VersionId)));
        Assert.Equal(
            "23505:ux_quote_responses_clarification",
            await RejectedByAsync(string.Format(Insert, "clarification_requested"), Row(world.Org, first.VersionId)));

        // A response, its contact and its version stay inside one organization.
        Assert.Equal(
            "23503:fk_quote_responses_quote_versions_organization_id_quote_versio",
            await RejectedByAsync(string.Format(Insert, "rejected"), Row(foreign.Org, second.VersionId)));
        Assert.Equal(
            "23503:fk_quote_responses_customer_contacts_organization_id_responder",
            await RejectedByAsync(string.Format(Insert, "rejected"), Row(world.Org, second.VersionId, foreign.Contact)));

        // A selected line belongs to the response's own version and organization; deleting the response removes it.
        var response = Guid.NewGuid();
        await database.ExecuteAsync(
            "INSERT INTO quote_responses (id, organization_id, quote_version_id, response, responder_name, subtotal, discount_total, tax_total, total) VALUES (@id, @o, @v, 'approved', 'Responder', 190, 10, 14.85, 194.85)",
            ("id", response),
            ("o", world.Org),
            ("v", second.VersionId));
        var ownLine = await database.ScalarAsync<Guid>(
            "SELECT id FROM quote_lines WHERE quote_version_id = @v AND is_optional ORDER BY sort_order LIMIT 1", ("v", second.VersionId));
        var otherVersionLine = await database.ScalarAsync<Guid>(
            "SELECT id FROM quote_lines WHERE quote_version_id = @v AND is_optional ORDER BY sort_order LIMIT 1", ("v", first.VersionId));
        var foreignLine = await database.ScalarAsync<Guid>(
            "SELECT id FROM quote_lines WHERE quote_version_id = @v AND is_optional ORDER BY sort_order LIMIT 1", ("v", other.VersionId));
        const string insertSelection =
            "INSERT INTO quote_response_optional_lines (organization_id, quote_version_id, quote_response_id, quote_line_id) VALUES (@o, @v, @r, @l)";

        Assert.StartsWith(
            "23503:fk_quote_response_optional_lines_quote_lines",
            await RejectedByAsync(insertSelection, ("o", world.Org), ("v", second.VersionId), ("r", response), ("l", otherVersionLine)));
        Assert.StartsWith(
            "23503:",
            await RejectedByAsync(insertSelection, ("o", foreign.Org), ("v", second.VersionId), ("r", response), ("l", foreignLine)));
        Assert.StartsWith(
            "23503:",
            await RejectedByAsync(insertSelection, ("o", world.Org), ("v", first.VersionId), ("r", response), ("l", otherVersionLine)));

        await database.ExecuteAsync(insertSelection, ("o", world.Org), ("v", second.VersionId), ("r", response), ("l", ownLine));
        Assert.Equal(
            "23505:pk_quote_response_optional_lines",
            await RejectedByAsync(insertSelection, ("o", world.Org), ("v", second.VersionId), ("r", response), ("l", ownLine)));
        await database.ExecuteAsync("DELETE FROM quote_responses WHERE id = @r", ("r", response));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_response_optional_lines WHERE quote_response_id = @r", ("r", response)));

        // The approved version belongs to the same quote and organization.
        Assert.Equal(
            "23503:fk_quotes_quote_versions_organization_id_id_approved_version_id",
            await RejectedByAsync("UPDATE quotes SET approved_version_id = @v WHERE id = @q", ("v", second.VersionId), ("q", first.QuoteId)));
        Assert.Equal(
            "23503:fk_quotes_quote_versions_organization_id_id_approved_version_id",
            await RejectedByAsync("UPDATE quotes SET approved_version_id = @v WHERE id = @q", ("v", other.VersionId), ("q", first.QuoteId)));
        await database.ExecuteAsync("UPDATE quotes SET approved_version_id = @v WHERE id = @q", ("v", first.VersionId), ("q", first.QuoteId));
    }
}
