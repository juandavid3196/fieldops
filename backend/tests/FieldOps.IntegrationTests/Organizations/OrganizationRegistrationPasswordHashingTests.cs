using System.Net;
using System.Text.RegularExpressions;
using FieldOps.Infrastructure.Authentication;
using FieldOps.IntegrationTests.Sessions;

namespace FieldOps.IntegrationTests.Organizations;

[Collection(SessionsDatabaseCollection.Name)]
public partial class OrganizationRegistrationPasswordHashingTests(SessionsDatabaseFixture database)
{
    // AC-14 (mandatory: password hashing and sensitive data).
    [Fact]
    public async Task PostOrganizationRegistrations_ValidRequest_StoresVerifiablePbkdf2Hash()
    {
        await using var host = OrganizationRegistrationTestHost.Create(database.ConnectionString);
        const string password = "correct horse battery";
        var email = OrganizationRegistrationApi.NewEmail();
        var body = OrganizationRegistrationApi.ValidBody(ownerEmail: email, password: password);

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var hash = await database.ScalarAsync<string>(
            "SELECT password_hash FROM users WHERE email = @email", ("email", email));

        Assert.NotNull(hash);
        Assert.Matches(HashFormat(), hash);
        Assert.True(new Pbkdf2PasswordHasher().Verify(password, hash!));
    }

    [GeneratedRegex(@"^pbkdf2-sha256\$600000\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+$")]
    private static partial Regex HashFormat();
}
