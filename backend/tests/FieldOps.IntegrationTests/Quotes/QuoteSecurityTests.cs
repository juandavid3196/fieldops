using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.ServiceRequests;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Roles, branch scope and tenant isolation of every quote endpoint family (quote-builder AC-26, FR-16).</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class QuoteSecurityTests(CompanySettingsDatabaseFixture database)
{
    [Fact]
    public async Task Endpoints_EnforceRolesBranchScopeAndOrganizationWithoutChangingData()
    {
        var world = await database.SeedQuoteWorldAsync();
        var foreign = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (owner, ownerMember) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (dispatcher, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher", world.BranchA);
        var (otherBranch, _) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Bea", "Bravo", world.BranchB);
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var (operations, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.OperationsManagerRoleId);
        var (accounting, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.AccountingRoleId);
        var (technician, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.TechnicianRoleId);
        var (foreignOwner, _) = await host.SignInAsync(database, foreign.Org, CompanySettingsDatabaseFixture.OwnerRoleId);

        // A sent quote of a branch A request whose completed assessment has a photo.
        var request = await database.SeedReadyRequestAsync(world);
        var assessment = await database.SeedAssessmentAsync(
            world.Org, request, DateTimeOffset.UtcNow.AddHours(-3), DateTimeOffset.UtcNow.AddHours(-2), ownerMember.UserId, status: "completed");
        await database.ExecuteAsync("UPDATE assessments SET diagnosis = 'Worn valve', completed_at = now() WHERE id = @a", ("a", assessment));
        var photo = Guid.NewGuid();
        var jpeg = CatalogSeed.Jpeg(100);
        await database.ExecuteAsync(
            "INSERT INTO assessment_attachments (id, organization_id, assessment_id, file_name, content, mime_type, size_bytes) VALUES (@id, @o, @a, 'seed.jpg', @c, 'image/jpeg', @s)",
            ("id", photo),
            ("o", world.Org),
            ("a", assessment),
            ("c", jpeg),
            ("s", (long)jpeg.Length));
        var quote = await QuotesApi.CreateAsync(host, owner, request);
        var sent = (await QuotesApi.SendAsync(host, owner, quote, QuotesApi.Draft([QuotesApi.Line()])))["quote"]!;
        var quoteId = sent["id"]!.GetValue<Guid>();
        var other = await database.SeedReadyRequestAsync(world);
        var started = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(
            world.Org, started.Id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), ownerMember.UserId);

        var reads = new[]
        {
            $"/quotes/{quoteId}",
            $"/quotes/{quoteId}/versions/1",
            $"/service-requests/{request}/assessments/{assessment}/photos/{photo}",
        };

        // Reads: owner, in-scope dispatcher and every read role see them; a Technician is 403; another branch
        // or organization is the same 404 as a missing id, in every family.
        foreach (var cookie in new[] { owner, dispatcher, viewer, operations, accounting })
        {
            foreach (var read in reads)
            {
                Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(HttpMethod.Get, read, cookie)).StatusCode);
            }
        }

        foreach (var read in reads)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(HttpMethod.Get, read, technician)).StatusCode);

            foreach (var outsider in new[] { otherBranch, foreignOwner })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, read, outsider)).StatusCode);
            }
        }

        Assert.True((await QuotesApi.GetAsync(host, owner, quoteId))["canManage"]!.GetValue<bool>());
        Assert.False((await QuotesApi.GetAsync(host, viewer, quoteId))["canManage"]!.GetValue<bool>());
        Assert.False((await QuotesApi.GetAsync(host, accounting, quoteId))["canManage"]!.GetValue<bool>());
        Assert.Equal(
            HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, $"/quotes/{Guid.NewGuid()}", owner)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized, (await host.SendAsync(HttpMethod.Get, $"/quotes/{quoteId}", null)).StatusCode);

        // Mutations and calculate: managers only. Read roles and the Technician are 403, outsiders are 404 (including
        // create by request id and completion), and nothing changes.
        var token = sent["updatedAt"]!.GetValue<string>();
        (HttpMethod Method, string Path, Func<JsonObject> Body)[] mutations =
        [
            (HttpMethod.Post, "/quotes", () => new JsonObject { ["requestId"] = other }),
            (HttpMethod.Post, $"/quotes/{quoteId}/calculate", () => QuotesApi.Draft([QuotesApi.Line()])),
            (HttpMethod.Put, $"/quotes/{quoteId}/draft", () => QuotesApi.Draft([QuotesApi.Line()]).With("updatedAt", token)),
            (HttpMethod.Post, $"/quotes/{quoteId}/send", () => QuotesApi.Draft([QuotesApi.Line()]).With("updatedAt", token).With("emailMessage", "Hi")),
            (HttpMethod.Post, $"/quotes/{quoteId}/revise", () => new JsonObject { ["updatedAt"] = token }),
            (HttpMethod.Post, $"/quotes/{quoteId}/discard-draft", () => new JsonObject { ["updatedAt"] = token }),
            (HttpMethod.Post, $"/quotes/{quoteId}/resend-email", () => new JsonObject { ["updatedAt"] = token, ["emailMessage"] = "Hi" }),
        ];

        var denied = new[] { (viewer, HttpStatusCode.Forbidden), (operations, HttpStatusCode.Forbidden), (accounting, HttpStatusCode.Forbidden), (technician, HttpStatusCode.Forbidden), (otherBranch, HttpStatusCode.NotFound), (foreignOwner, HttpStatusCode.NotFound) };

        foreach (var (method, path, body) in mutations)
        {
            foreach (var (cookie, expected) in denied)
            {
                // The foreign owner cannot see the quote, so its create targets the foreign-organization request id.
                var response = await host.SendAsync(method, path, cookie, body());

                Assert.Equal(expected, response.StatusCode);
            }
        }

        foreach (var (cookie, expected) in denied)
        {
            var response = await host.CompleteAssessmentAsync(started.Id, cookie);

            Assert.Equal(expected, response.StatusCode);
        }

        Assert.Equal("assessment_scheduled", await database.StatusOfAsync(started.Id));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quotes WHERE request_id = @r", ("r", other)));
        Assert.Equal(token, (await QuotesApi.GetAsync(host, owner, quoteId))["updatedAt"]!.GetValue<string>());
        Assert.Equal("sent", await database.ScalarAsync<string>("SELECT status::text FROM quotes WHERE id = @q", ("q", quoteId)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_versions WHERE quote_id = @q", ("q", quoteId)));
        Assert.Equal(1, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quote_access_tokens WHERE organization_id = @o", ("o", world.Org)));

        // A manager in scope and the owner can create; a foreign-organization request id is 404 even for an owner.
        Assert.Equal(HttpStatusCode.Created, (await host.SendAsync(HttpMethod.Post, "/quotes", dispatcher, new JsonObject { ["requestId"] = other })).StatusCode);
        var foreignRequest = await database.SeedReadyRequestAsync(foreign);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Post, "/quotes", owner, new JsonObject { ["requestId"] = foreignRequest })).StatusCode);
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM quotes WHERE request_id = @r", ("r", foreignRequest)));
    }
}
