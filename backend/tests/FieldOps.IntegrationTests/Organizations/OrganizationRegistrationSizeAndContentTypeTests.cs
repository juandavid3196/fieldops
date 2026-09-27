using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationSizeAndContentTypeTests(SessionsDatabaseFixture database)
{
    // AC-18. The request size limit is enforced by the real server, so this
    // test runs on Kestrel instead of the in-memory test server.
    [Fact]
    public async Task PostOrganizationRegistrations_BodyLargerThan32Kilobytes_Returns413AndCreatesNoRows()
    {
        var before = await database.ScalarAsync<long>("SELECT count(*) FROM organizations");
        await using var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString);
        factory.UseKestrel(0);
        factory.StartServer();
        using var client = factory.CreateClient();

        var oversizedName = new string('a', 40 * 1024);
        var body = OrganizationRegistrationApi.ValidBody(organizationName: oversizedName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/organization-registrations")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await database.ScalarAsync<long>("SELECT count(*) FROM organizations"));
    }

    // AC-35.
    [Fact]
    public async Task PostOrganizationRegistrations_TextPlainContentType_Returns415AndCreatesNoRows()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var before = await database.ScalarAsync<long>("SELECT count(*) FROM organizations");

        var response = await OrganizationRegistrationApi.PostAsync(
            host.Client, OrganizationRegistrationApi.ValidBody(), mediaType: "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await database.ScalarAsync<long>("SELECT count(*) FROM organizations"));
    }

    // AC-36.
    [Theory]
    [InlineData("not json")]
    [InlineData("""{"organization":""")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task PostOrganizationRegistrations_MalformedOrIncompleteBody_Returns400WithTraceIdAndNoFieldKeysAndCreatesNoRows(
        string body)
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var before = await database.ScalarAsync<long>("SELECT count(*) FROM organizations");

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(text);
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.False(problem.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(before, await database.ScalarAsync<long>("SELECT count(*) FROM organizations"));
    }

    // AC-11 wiring: the theory-level validator coverage lives in
    // RegisterOrganizationCommandValidatorTests; this proves the endpoint
    // actually wires the validator's errors into the 400 body.
    [Fact]
    public async Task PostOrganizationRegistrations_MissingOrganizationName_Returns400WithFieldKeyAndMessage()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        var json = JsonNode.Parse(OrganizationRegistrationApi.ValidBody())!.AsObject();
        json["organization"]!["name"] = "";

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, json.ToJsonString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(text);
        var errors = problem.RootElement.GetProperty("errors");
        var property = Assert.Single(errors.EnumerateObject(), p => p.Name == "organization.name");
        Assert.Equal("This field is required.", Assert.Single(property.Value.EnumerateArray()).GetString());
    }
}
