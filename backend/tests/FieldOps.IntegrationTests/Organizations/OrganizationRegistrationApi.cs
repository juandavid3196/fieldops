using System.Text;
using FieldOps.IntegrationTests.Api;

namespace FieldOps.IntegrationTests.Organizations;

/// <summary>
/// HTTP helpers for POST /organization-registrations.
/// </summary>
public static class OrganizationRegistrationApi
{
    private static int s_nextClientIp;

    /// <summary>A client IP no other request used, so per-IP limits never interfere.</summary>
    public static string NewClientIp()
    {
        var next = Interlocked.Increment(ref s_nextClientIp);
        return $"10.{(next >> 16) & 0xFF}.{(next >> 8) & 0xFF}.{next & 0xFF}";
    }

    public static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string body,
        string? mediaType = "application/json",
        string? clientIp = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/organization-registrations")
        {
            Content = mediaType is null
                ? new ByteArrayContent(Encoding.UTF8.GetBytes(body))
                : new StringContent(body, Encoding.UTF8, mediaType),
        };

        request.Headers.Add(TestClientIpStartupFilter.HeaderName, clientIp ?? NewClientIp());

        return client.SendAsync(request);
    }

    /// <summary>A fully valid registration body per BR-01, with overridable owner email/password.</summary>
    public static string ValidBody(
        string? ownerEmail = null,
        string? password = null,
        string organizationName = "Acme Field Services",
        string branchCode = "MAIN") =>
        $$"""
        {
          "organization": {
            "name": "{{organizationName}}",
            "legalName": "Acme Field Services LLC",
            "taxId": "12-3456789",
            "email": "ops@acme.com",
            "phone": "+1 555 123 4567",
            "timezone": "America/Chicago",
            "currency": "USD",
            "defaultTaxRate": 7.25,
            "quotePrefix": "Q",
            "workOrderPrefix": "WO",
            "invoicePrefix": "INV",
            "nextInvoiceNumber": 1
          },
          "branch": {
            "name": "Main Branch",
            "code": "{{branchCode}}",
            "phone": "+1 555 987 6543",
            "email": "branch@acme.com",
            "timezone": "America/Chicago",
            "addressLine1": "123 Main St",
            "city": "Chicago",
            "stateRegion": "IL",
            "postalCode": "60601",
            "countryCode": "US",
            "businessHours": { "monday": { "start": "08:00", "end": "17:00" } }
          },
          "owner": {
            "firstName": "Ada",
            "lastName": "Lovelace",
            "email": "{{ownerEmail ?? NewEmail()}}",
            "password": "{{password ?? "correct horse battery"}}",
            "phone": "+1 555 111 2222"
          }
        }
        """;
}
