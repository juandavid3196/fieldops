using System.Globalization;
using System.Net;

namespace FieldOps.IntegrationTests.Organizations;

/// <summary>
/// Per-IP and global limits need no database: an invalid body is rejected
/// with 400 but still counts toward the limits (BR-21).
/// </summary>
public class OrganizationRegistrationRateLimitTests
{
    private const string InvalidBody = "{}";

    // AC-16.
    [Fact]
    public async Task PostOrganizationRegistrations_6thRequestFromOneIpIn15MinWindow_Returns429WithRetryAfter()
    {
        await using var host = OrganizationRegistrationTestHost.Create();
        var clientIp = OrganizationRegistrationApi.NewClientIp();

        for (var i = 0; i < 5; i++)
        {
            var allowed = await OrganizationRegistrationApi.PostAsync(host.Client, InvalidBody, clientIp: clientIp);
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        var response = await OrganizationRegistrationApi.PostAsync(host.Client, InvalidBody, clientIp: clientIp);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertRetryAfter(response, maxSeconds: 900);

        // Other clients are not affected by this client's limit.
        var other = await OrganizationRegistrationApi.PostAsync(host.Client, InvalidBody);
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    // AC-17.
    [Fact]
    public async Task PostOrganizationRegistrations_After100RequestsInOneHour_Returns429ForAnyClient()
    {
        await using var host = OrganizationRegistrationTestHost.Create();

        for (var client = 0; client < 20; client++)
        {
            var clientIp = OrganizationRegistrationApi.NewClientIp();

            for (var i = 0; i < 5; i++)
            {
                var allowed = await OrganizationRegistrationApi.PostAsync(host.Client, InvalidBody, clientIp: clientIp);
                Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
            }
        }

        var response = await OrganizationRegistrationApi.PostAsync(
            host.Client, InvalidBody, clientIp: OrganizationRegistrationApi.NewClientIp());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        AssertRetryAfter(response, maxSeconds: 3600);
    }

    private static void AssertRetryAfter(HttpResponseMessage response, int maxSeconds)
    {
        var value = Assert.Single(response.Headers.GetValues("Retry-After"));
        var seconds = int.Parse(value, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 1, maxSeconds);
    }
}
