using System.Net;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public class OrganizationRegistrationLoggingTests(SessionsDatabaseFixture database)
{
    // AC-15 (mandatory: no log leakage of sensitive request data).
    [Fact]
    public async Task PostOrganizationRegistrations_ValidAndInvalidRequests_NeverLogSensitiveValues()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString, captureLogs: true);
        const string password = "correct horse battery";
        var email = OrganizationRegistrationApi.NewEmail();
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: email, password: password);

        var success = await OrganizationRegistrationApi.PostAsync(host.Client, body);
        Assert.Equal(HttpStatusCode.Created, success.StatusCode);

        var invalid = await OrganizationRegistrationApi.PostAsync(host.Client, "{}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var duplicate = await OrganizationRegistrationApi.PostAsync(
            host.Client, OrganizationRegistrationApi.ValidBody(ownerEmail: email, password: password));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var forbidden = new[] { email, password, "acme.com", "555 111 2222", "123 Main St" };
        var entries = host.Logs!.Entries;
        Assert.NotEmpty(entries);

        foreach (var entry in entries)
        {
            var text = string.Join(
                "\n",
                new[] { entry.Message, entry.Exception?.ToString() }
                    .Concat(entry.Properties.Values.Select(value => value?.ToString())));

            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
