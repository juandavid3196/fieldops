using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.AuditTrail;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static FieldOps.IntegrationTests.AuditLog.AuditSeed;

namespace FieldOps.IntegrationTests.AuditLog;

/// <summary><c>export.generated</c> and <c>auth.signed_out</c> (audit-trail AC-15, AC-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AuditLogWriteTests(CompanySettingsDatabaseFixture database)
{
    private const string Needle = "zebra-needle";

    // AC-15: each of the six exports writes exactly one normalized row after success, none on a 400/404, and a failed insert is a 500 with no file.
    [Fact]
    public async Task Exports_RecordOneNormalizedRowAfterSuccess_AndNothingOtherwise()
    {
        var world = await database.SeedWorldAsync("UTC");
        var foreign = await database.SeedWorldAsync();
        var owner = await database.SeedMemberAsync(world.Org, Owner, "Olivia", "Owner");
        await using var host = ToggleHost.Create(database);
        var cookie = await CompanySettingsApi.SignInCookieAsync(host.Client, owner.Email);
        await database.ClearAuditAsync(world.Org);

        var from = LocalDay("UTC", -30);
        var to = LocalDay("UTC");
        var branch = world.BranchA.ToString();

        var cases = new (string Report, string Path, string Bad, string Filters, Guid? Branch, bool WithRows)[]
        {
            (
                "invoices",
                $"/invoices/export?from={from}&to={to}&status=sent&branchId={branch}&search={Needle}",
                "/invoices/export?from=not-a-date",
                $$"""{"from":"{{from}}","to":"{{to}}","status":"sent","customerId":null,"branchId":"{{branch}}","hasSearch":true}""",
                world.BranchA,
                false),
            (
                "payments",
                $"/invoices/payments/export?from={from}&to={to}&method=cash",
                "/invoices/payments/export?method=bogus",
                $$"""{"from":"{{from}}","to":"{{to}}","method":"cash","branchId":null,"hasSearch":false}""",
                null,
                false),
            (
                "billing_review",
                $"/billing-review/export?completed=30d&variance=all&tab=all&search={Needle}",
                "/billing-review/export?tab=bogus",
                """{"completed":"30d","technicianId":null,"variance":"all","tab":"all","branchId":null,"hasSearch":true}""",
                null,
                false),
            (
                "business_overview",
                $"/business-overview/export?from={from}&to={to}&compare=false",
                "/business-overview/export?from=2026-13-01&to=" + to,
                $$"""{"from":"{{from}}","to":"{{to}}","branchId":null,"compare":false,"hasSearch":false}""",
                null,
                true),
            (
                "catalog_items",
                $"/catalog-items/export?type=service&status=active&sort=-name&search={Needle}",
                "/catalog-items/export?type=bogus",
                """{"type":"service","taxable":null,"active":true,"sort":"name","descending":true,"hasSearch":true}""",
                null,
                false),
            (
                "audit_log",
                $"/audit-log/export?q={Needle}&sort=impact&direction=asc&branchId={branch}",
                "/audit-log/export?sort=bogus",
                $$"""{"from":"{{to}}","to":"{{to}}","actorId":null,"action":null,"entityType":null,"entityId":null,"branchId":"{{branch}}","impact":null,"sort":"impact","direction":"asc","hasSearch":true}""",
                world.BranchA,
                false),
        };

        Task<long> TotalAsync() => database.CountAuditAsync(world.Org, "export.generated");

        foreach (var (report, path, bad, filters, expectedBranch, withRows) in cases)
        {
            var before = await TotalAsync();
            var response = await host.SendAsync(path, cookie);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{report}: {(int)response.StatusCode} {body}");
            Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
            Assert.Equal(before + 1, await TotalAsync());

            var row = await database.ScalarAsync<string>(
                """
                SELECT concat_ws('|', entity_type, COALESCE(entity_id::text, '-'), actor_user_id::text, COALESCE(branch_id::text, '-'),
                    COALESCE(before_data::text, '-'), COALESCE(after_data::text, '-'), (ip_address IS NOT NULL)::text)
                FROM audit_logs WHERE organization_id = @o AND action = 'export.generated' AND metadata->>'reportType' = @t ORDER BY id DESC LIMIT 1
                """,
                ("o", world.Org),
                ("t", report));
            Assert.Equal($"export|-|{owner.UserId}|{expectedBranch?.ToString() ?? "-"}|-|-|true", row);

            var metadataText = await database.ScalarAsync<string>(
                "SELECT metadata::text FROM audit_logs WHERE organization_id = @o AND action = 'export.generated' AND metadata->>'reportType' = @t ORDER BY id DESC LIMIT 1",
                ("o", world.Org),
                ("t", report));
            var metadata = JsonNode.Parse(metadataText)!;
            Assert.Equal(report, metadata["reportType"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(filters), metadata["filters"]), $"{report}: {metadata["filters"]}");
            Assert.Equal(AuditLogContentTests.Records(body).Count - 1, metadata["rowCount"]!.GetValue<int>());
            Assert.DoesNotContain(Needle, metadataText, StringComparison.Ordinal);

            if (withRows)
            {
                Assert.True(metadata["rowCount"]!.GetValue<int>() > 0);
            }

            // BR-16: a rejected request writes nothing.
            var rejected = await host.SendAsync(bad, cookie);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before + 1, await TotalAsync());

            // A failed insert is a 500 without a file and without a row.
            host.State.Fail = true;
            var failed = await host.SendAsync(path, cookie);
            host.State.Fail = false;
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.DoesNotContain("text/csv", failed.Content.Headers.ContentType?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(before + 1, await TotalAsync());
        }

        // A foreign branch is a 404 that writes nothing.
        var total = await TotalAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync($"/invoices/export?branchId={foreign.BranchA}", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync($"/audit-log/export?branchId={foreign.BranchA}", cookie)).StatusCode);
        Assert.Equal(total, await TotalAsync());
    }

    // AC-16: a valid session writes one auth.signed_out row; no session or a broken one writes none and the response is the same.
    [Fact]
    public async Task SignOut_WithAValidSession_RecordsOneRow_AndOtherwiseNothing()
    {
        var world = await database.SeedWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (cookie, member) = await host.SignInAsync(database, world.Org, Owner, "Olivia", "Owner");
        await database.ClearAuditAsync(world.Org);

        var anonymous = await SessionApi.DeleteCurrentAsync(host.Client);
        var broken = await SessionApi.DeleteCurrentAsync(host.Client, $"{SessionApi.CookieName}=not-a-ticket");
        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (anonymous.StatusCode, broken.StatusCode));
        Assert.Equal(0, await database.CountAuditAsync(world.Org, "auth.signed_out"));

        var response = await SessionApi.DeleteCurrentAsync(host.Client, cookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        SessionApi.AssertCookieDeleted(response);
        Assert.Equal(await anonymous.Content.ReadAsStringAsync(), await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await database.CountAuditAsync(world.Org, "auth.signed_out"));

        var row = await database.ScalarAsync<string>(
            """
            SELECT concat_ws('|', entity_type, entity_id::text, actor_user_id::text, organization_id::text, COALESCE(branch_id::text, '-'),
                COALESCE(before_data::text, '-'), COALESCE(after_data::text, '-'), metadata::text, (ip_address IS NOT NULL)::text)
            FROM audit_logs WHERE organization_id = @o AND action = 'auth.signed_out'
            """,
            ("o", world.Org));
        Assert.Equal($"user|{member.UserId}|{member.UserId}|{world.Org}|-|-|-|{{}}|true", row);

        // The event is readable like any other (BR-01): label, source and impact come from the catalog.
        var page = await GetAsync(host, cookie, "/audit-log?action=auth.signed_out");
        var item = Assert.Single(page["items"]!.AsArray())!;
        Assert.Equal(("Signed out", "Olivia Owner", "Web app", "low"),
            (item["action"]!["label"]!.GetValue<string>(), item["actor"]!["name"]!.GetValue<string>(), item["source"]!.GetValue<string>(), item["impact"]!.GetValue<string>()));
    }

    private sealed class FailureState
    {
        public bool Fail { get; set; }
    }

    private sealed class ToggleWriter(FailureState state, IExportAuditWriter inner) : IExportAuditWriter
    {
        public Task RecordAsync(FieldOps.Application.Features.ServiceRequests.MembershipCall call, ExportAuditEntry entry, CancellationToken cancellationToken) =>
            state.Fail
                ? throw new InvalidOperationException("The audit insert failed.")
                : inner.RecordAsync(call, entry, cancellationToken);
    }

    /// <summary>One API host whose export audit writer can be told to fail, to prove the BR-16 failure rule.</summary>
    private sealed class ToggleHost : IAsyncDisposable
    {
        private readonly SessionTestHost _host;

        private ToggleHost(SessionTestHost host, HttpClient client, FailureState state)
        {
            _host = host;
            Client = client;
            State = state;
        }

        public HttpClient Client { get; }

        public FailureState State { get; }

        public static ToggleHost Create(CompanySettingsDatabaseFixture db)
        {
            var host = SessionTestHost.Create(db.ConnectionString);
            var state = new FailureState();
            var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                var original = services.Last(descriptor => descriptor.ServiceType == typeof(IExportAuditWriter));
                services.Remove(original);
                services.AddScoped<IExportAuditWriter>(provider =>
                    new ToggleWriter(state, (IExportAuditWriter)original.ImplementationFactory!(provider)));
            }));

            return new ToggleHost(host, SessionApi.CreateClient(factory), state);
        }

        public Task<HttpResponseMessage> SendAsync(string path, string cookie) =>
            CompanySettingsApi.SendRawAsync(Client, HttpMethod.Get, path, null, cookie);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.DisposeAsync();
        }
    }
}
