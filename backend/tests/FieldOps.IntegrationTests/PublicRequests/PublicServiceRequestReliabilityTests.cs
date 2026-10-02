using System.Globalization;
using System.Net;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.PublicRequests;

[Collection(CompanySettingsDatabaseCollection.Name)]
public class PublicServiceRequestReliabilityTests(CompanySettingsDatabaseFixture database)
{
    private const string FailingConstraint = "ck_test_force_public_property_failure";

    // AC-17, AC-19 (failure tolerance): rollback after the customer exists, and a throwing email sender.
    [Fact]
    public async Task Submit_FailureMidTransactionRollsBackEverything_AndEmailFailureStillCreatesTheRequest()
    {
        var org = await database.SeedPublicOrgAsync();
        await using var host = PublicRequestHost.Create(database, captureLogs: true);
        var before = await database.SnapshotAsync(org.Id);

        // The check only guards this organization's property rows, so it fails after the customer is written.
        await database.ExecuteAsync(
            $"ALTER TABLE properties ADD CONSTRAINT {FailingConstraint} CHECK (organization_id <> '{org.Id}' OR name NOT IN ('Home', 'Business')) NOT VALID");

        HttpResponseMessage failed;

        try
        {
            failed = await host.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org), [("a.jpg", PublicRequestSeed.Jpeg())]);
        }
        finally
        {
            await database.ExecuteAsync($"ALTER TABLE properties DROP CONSTRAINT {FailingConstraint}");
        }

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Equal(before, await database.SnapshotAsync(org.Id));
        Assert.Equal(1L, await database.NextNumberAsync(org.Id));
        Assert.Equal(0, host.Sender.Attempts);

        // The failing row's personal data never reaches the logs.
        AssertLogsHold(host, "Analytical", "visitor@example.com", "Lovelace", org.Slug);

        // A provider outage after the commit does not change the response.
        host.Sender.Fail = true;
        var created = await host.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("REQ-1", (await PublicRequestSeed.ReadAsync(created))["requestNumber"]!.GetValue<string>());
        Assert.Equal(1, host.Sender.Attempts);

        var requestId = await database.ScalarAsync<Guid>(
            "SELECT id FROM service_requests WHERE organization_id = @o", ("o", org.Id));
        var warning = Assert.Single(
            host.Logs!.Entries,
            entry => entry.Category.EndsWith("PublicRequestConfirmationSender", StringComparison.Ordinal));
        Assert.Contains(requestId.ToString(), warning.Message, StringComparison.Ordinal);
        Assert.Null(warning.Exception);
        AssertLogsHold(host, "visitor@example.com", "Lovelace", "Analytical", org.Slug);
    }

    // AC-12: consecutive numbers per organization under concurrency; the other organization is independent.
    [Fact]
    public async Task Submit_ConcurrentRequests_GetDistinctConsecutiveNumbersPerOrganization()
    {
        var first = await database.SeedPublicOrgAsync();
        var second = await database.SeedPublicOrgAsync();
        await database.ExecuteAsync(
            "INSERT INTO service_requests (organization_id, request_number, description, status) VALUES (@o, 41, 'prior', 'new'); UPDATE organizations SET next_request_number = 42 WHERE id = @o",
            ("o", first.Id));
        await database.ExecuteAsync(
            "UPDATE organizations SET request_prefix = 'SRV' WHERE id = @o", ("o", second.Id));

        await using var host = PublicRequestHost.Create(database);

        var firstResponses = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
            await host.PostAsync(first.Slug, PublicRequestSeed.ValidBody(first, $"concurrent{index}@example.com"))));
        var secondResponse = await host.PostAsync(second.Slug, PublicRequestSeed.ValidBody(second));

        Assert.All(firstResponses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));

        var numbers = new List<string>();

        foreach (var response in firstResponses)
        {
            numbers.Add((await PublicRequestSeed.ReadAsync(response))["requestNumber"]!.GetValue<string>());
        }

        Assert.Equal(
            Enumerable.Range(42, 6).Select(n => $"REQ-{n.ToString(CultureInfo.InvariantCulture)}"),
            numbers.Order(StringComparer.Ordinal));
        Assert.Equal(48L, await database.NextNumberAsync(first.Id));

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal("SRV-1", (await PublicRequestSeed.ReadAsync(secondResponse))["requestNumber"]!.GetValue<string>());
        Assert.Equal(2L, await database.NextNumberAsync(second.Id));
        Assert.Equal(1L, await database.CountAsync("service_requests", second.Id));
    }

    // AC-20, AC-21: honeypot and per-IP rate limits for both endpoints.
    [Fact]
    public async Task PublicEndpoints_HoneypotAndPerIpLimits_ReturnGeneric400AndThen429WithRetryAfter()
    {
        var org = await database.SeedPublicOrgAsync();
        await using var host = PublicRequestHost.Create(database);
        var before = await database.SnapshotAsync(org.Id);

        // BR-17: a filled honeypot is a generic 400 (no field errors) and persists nothing.
        var bot = PublicRequestSeed.ValidBody(org);
        bot["website"] = "https://spam.example";
        var honeypot = await host.PostAsync(org.Slug, bot);

        Assert.Equal(HttpStatusCode.BadRequest, honeypot.StatusCode);
        Assert.Null((await PublicRequestSeed.ReadAsync(honeypot))["errors"]);
        Assert.Equal(before, await database.SnapshotAsync(org.Id));
        Assert.Equal(0, host.Sender.Attempts);

        // BR-16: submission 5 per IP per 15 minutes; every request counts, whatever its outcome.
        var submitIp = SessionApi.NewClientIp();

        for (var i = 0; i < 5; i++)
        {
            var allowed = await host.PostAsync(org.Slug, bot, ip: submitIp);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        var limited = await host.PostAsync(org.Slug, PublicRequestSeed.ValidBody(org), ip: submitIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        AssertRetryAfter(limited, maxSeconds: 900);
        Assert.Equal(before, await database.SnapshotAsync(org.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(org.Slug, bot)).StatusCode);

        // Form configuration: 60 per IP per 5 minutes.
        var formIp = SessionApi.NewClientIp();

        for (var i = 0; i < 60; i++)
        {
            var allowed = await host.GetFormAsync(org.Slug, formIp);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var formLimited = await host.GetFormAsync(org.Slug, formIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, formLimited.StatusCode);
        AssertRetryAfter(formLimited, maxSeconds: 300);
    }

    private static void AssertRetryAfter(HttpResponseMessage response, int maxSeconds)
    {
        var value = Assert.Single(response.Headers.GetValues("Retry-After"));

        Assert.InRange(int.Parse(value, CultureInfo.InvariantCulture), 1, maxSeconds);
    }

    private static void AssertLogsHold(PublicRequestHost host, params string[] forbidden)
    {
        foreach (var entry in host.Logs!.Entries)
        {
            var text = $"{entry.Message} {entry.Exception} {string.Join(' ', entry.Properties.Values)}";

            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
