using System.Net;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.Catalog;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.PublicRequests;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Team;

namespace FieldOps.IntegrationTests.Quotes;

/// <summary>Assessment findings (quote-builder AC-01, AC-02): multipart completion, photos, validation, 413 and 415.</summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class AssessmentFindingsTests(CompanySettingsDatabaseFixture database)
{
    private const string PhotosMessage = "Photos must be JPG or PNG files of 10 MB or less.";

    // AC-01, AC-02: one transaction writes findings, photos, status and audit; every invalid input persists nothing.
    [Fact]
    public async Task CompleteAssessment_PersistsFindingsAndPhotosInOneTransactionAndRejectsInvalidInput()
    {
        var world = await database.SeedQuoteWorldAsync();
        await using var host = RequestsHost.Create(database);
        var (dispatcher, member) = await host.SignInAsync(
            database, world.Org, CompanySettingsDatabaseFixture.DispatcherRoleId, "Dina", "Dispatcher");
        var (viewer, _) = await host.SignInAsync(database, world.Org, CompanySettingsDatabaseFixture.ViewerRoleId);
        var technician = await database.SeedTechAsync(world.Org, world.BranchA, "Tess", "Tech");

        var request = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        var assessment = await database.SeedAssessmentAsync(
            world.Org, request.Id, DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), member.UserId, technician);

        // Invalid input: each case is a 400 on its key (the oversized body is a 413) and nothing is persisted.
        var big = CatalogSeed.Jpeg(10 * 1024 * 1024 + 1);
        var cases = new (string Key, string Message, Func<Task<HttpResponseMessage>> Send)[]
        {
            ("diagnosis", "Enter the diagnosis.", () => host.CompleteAssessmentAsync(request.Id, dispatcher, diagnosis: null, recommendedScope: "Scope")),
            ("diagnosis", "Enter the diagnosis.", () => host.CompleteAssessmentAsync(request.Id, dispatcher, diagnosis: "   ")),
            ("diagnosis", "Diagnosis must be 2000 characters or fewer.", () => host.CompleteAssessmentAsync(request.Id, dispatcher, new string('d', 2001))),
            ("recommendedScope", "Recommended scope must be 2000 characters or fewer.", () => host.CompleteAssessmentAsync(request.Id, dispatcher, "Leak", new string('s', 2001))),
            ("photos", "Add up to 6 photos.", () => host.CompleteAssessmentAsync(
                request.Id, dispatcher, "Leak", null, [.. Enumerable.Range(0, 7).Select(index => ($"p{index}.jpg", CatalogSeed.Jpeg()))])),
            ("photos", PhotosMessage, () => host.CompleteAssessmentAsync(request.Id, dispatcher, "Leak", null, ("notes.pdf", PublicRequestSeed.Pdf(200)))),
            ("photos", PhotosMessage, () => host.CompleteAssessmentAsync(request.Id, dispatcher, "Leak", null, ("wrong.png", CatalogSeed.Jpeg()))),
            ("photos", PhotosMessage, () => host.CompleteAssessmentAsync(request.Id, dispatcher, "Leak", null, ("empty.jpg", []))),
            ("photos", PhotosMessage, () => host.CompleteAssessmentAsync(request.Id, dispatcher, "Leak", null, ("large.jpg", big))),
            ("photos", PhotosMessage, () => host.CompleteAssessmentAsync(
                request.Id, dispatcher, "Leak", null, [.. Enumerable.Range(0, 3).Select(index => ($"p{index}.jpg", CatalogSeed.Jpeg(8_800_000)))])),
        };

        foreach (var (key, message, send) in cases)
        {
            var response = await send();

            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{key}: {(int)response.StatusCode} {text}");
            Assert.True(text.Contains($"\"{key}\"", StringComparison.Ordinal), $"{key}: {text}");
            Assert.Equal(message, QuotesApi.Error(System.Text.Json.Nodes.JsonNode.Parse(text)!, key));
        }

        var json = await host.SendAsync(HttpMethod.Post, $"/service-requests/{request.Id}/assessment/complete", dispatcher, new JsonObject { ["diagnosis"] = "Leak" });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, json.StatusCode);

        var future = await database.SeedRequestAsync(world, status: "assessment_scheduled", branch: world.BranchA);
        await database.SeedAssessmentAsync(
            world.Org, future.Id, DateTimeOffset.UtcNow.AddHours(5), DateTimeOffset.UtcNow.AddHours(6), member.UserId);
        var notStarted = await host.CompleteAssessmentAsync(future.Id, dispatcher);
        Assert.Equal(HttpStatusCode.Conflict, notStarted.StatusCode);
        Assert.Equal("This assessment hasn't started yet.", (await RequestsHost.ReadAsync(notStarted))["title"]!.GetValue<string>());

        await using (var kestrel = FieldOpsApiFactory.Create(connectionString: database.ConnectionString))
        {
            kestrel.UseKestrel(0);
            kestrel.StartServer();
            kestrel.ClientOptions.HandleCookies = false;
            kestrel.ClientOptions.AllowAutoRedirect = false;
            using var client = kestrel.CreateClient();
            var kestrelCookie = await CompanySettingsApi.SignInCookieAsync(client, member.Email);
            var raw = await SendHeadersOnlyAsync(
                client.BaseAddress!, $"/service-requests/{request.Id}/assessment/complete", kestrelCookie, 27_262_976L + 1);

            Assert.StartsWith("HTTP/1.1 413", raw, StringComparison.Ordinal);
        }

        Assert.Equal("assessment_scheduled", await database.StatusOfAsync(request.Id));
        Assert.Equal("scheduled", await database.ScalarAsync<string>("SELECT status::text FROM assessments WHERE id = @a", ("a", assessment)));
        Assert.Equal(0, await database.ScalarAsync<long>("SELECT COUNT(*) FROM assessment_attachments WHERE assessment_id = @a", ("a", assessment)));
        Assert.Equal(0, await database.AuditCountAsync(request.Id, "service_request.assessment_completed"));
        Assert.Equal("assessment_scheduled", await database.StatusOfAsync(future.Id));

        // Success: findings trimmed, scope optional, two photos stored inline with the detected type.
        var jpeg = CatalogSeed.Jpeg(300);
        var png = CatalogSeed.Png(120);
        var completed = await host.CompleteAssessmentAsync(
            request.Id, dispatcher, "  Burst supply line under the sink  ", "Replace the line", ("kitchen.JPG", jpeg), ("after.png", png));
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var detail = await RequestsHost.ReadAsync(completed);

        Assert.Equal("ready_for_quote", detail["status"]!.GetValue<string>());
        var findings = detail["completedAssessment"]!;
        Assert.Equal(assessment, findings["id"]!.GetValue<Guid>());
        Assert.Equal("Burst supply line under the sink", findings["diagnosis"]!.GetValue<string>());
        Assert.Equal("Replace the line", findings["recommendedScope"]!.GetValue<string>());
        Assert.Equal("Tess Tech", findings["technician"]!["name"]!.GetValue<string>());
        Assert.Equal(["image/jpeg", "image/png"], findings["photos"]!.AsArray().Select(photo => photo!["mimeType"]!.GetValue<string>()).Order());
        Assert.Null(detail["quote"]);
        Assert.DoesNotContain("content", findings["photos"]![0]!.AsObject().Select(pair => pair.Key));

        Assert.Equal("ready_for_quote", await database.StatusOfAsync(request.Id));
        Assert.Equal(2, await database.ScalarAsync<long>(
            "SELECT COUNT(*) FROM assessment_attachments WHERE assessment_id = @a AND organization_id = @o AND content IS NOT NULL AND storage_key IS NULL",
            ("a", assessment),
            ("o", world.Org)));
        Assert.Equal(jpeg.Length + png.Length, await database.ScalarAsync<long>(
            "SELECT SUM(size_bytes)::bigint FROM assessment_attachments WHERE assessment_id = @a", ("a", assessment)));
        var (_, _, metadata) = await database.GetLatestAuditAsync(world.Org, "service_request.assessment_completed");
        Assert.Equal(2, JsonNode.Parse(metadata!)!["photoCount"]!.GetValue<int>());
        var audit = await database.AuditTextAsync(request.Id);
        Assert.DoesNotContain("Burst", audit);
        Assert.DoesNotContain("kitchen", audit);

        // Download: read roles, BR-03 headers and exact content; a foreign assessment id is a 404.
        var photoId = findings["photos"]!.AsArray().Single(photo => photo!["mimeType"]!.GetValue<string>() == "image/jpeg")!["id"]!.GetValue<Guid>();
        var download = await host.SendAsync(HttpMethod.Get, $"/service-requests/{request.Id}/assessments/{assessment}/photos/{photoId}", viewer);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("inline", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(download.Headers.CacheControl is { NoStore: true, Private: true });
        Assert.Equal(jpeg, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"/service-requests/{request.Id}/assessments/{Guid.NewGuid()}/photos/{photoId}", viewer)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await host.SendAsync(HttpMethod.Get, $"/service-requests/{future.Id}/assessments/{assessment}/photos/{photoId}", viewer)).StatusCode);
    }

    private static async Task<string> SendHeadersOnlyAsync(Uri baseAddress, string path, string cookie, long contentLength)
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port);
        await using var stream = tcp.GetStream();

        var head =
            $"POST {path} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\nCookie: {cookie}\r\n"
            + $"{TestClientIpStartupFilter.HeaderName}: {Sessions.SessionApi.NewClientIp()}\r\n"
            + $"Content-Type: multipart/form-data; boundary=fieldopsboundary\r\nContent-Length: {contentLength}\r\n"
            + "Connection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));

        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        return await reader.ReadToEndAsync(timeout.Token);
    }
}
