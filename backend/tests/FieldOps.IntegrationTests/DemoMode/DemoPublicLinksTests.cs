using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.Application.Features.OnlinePayments;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.BillingReview;
using FieldOps.IntegrationTests.OnlinePayments;
using FieldOps.IntegrationTests.Organizations;
using FieldOps.IntegrationTests.PasswordResets;
using FieldOps.IntegrationTests.QuoteLinks;
using FieldOps.IntegrationTests.Quotes;
using FieldOps.IntegrationTests.ServiceRequests;
using FieldOps.IntegrationTests.Sessions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.DemoMode;

/// <summary>
/// Anonymous quote and invoice links and the Stripe webhook for a read-only demo organization: reads and downloads work,
/// every write is refused before any effect, and organizations outside the demo (including newly registered ones) keep
/// their full behaviour on the same host.
/// </summary>
[Collection(CompanySettingsDatabaseCollection.Name)]
public class DemoPublicLinksTests(CompanySettingsDatabaseFixture database)
{
    private const string DemoCode = "\"code\":\"demo_read_only\"";

    private static string NewEvent() => $"evt_{Guid.NewGuid():N}";

    [Fact]
    public async Task DemoQuoteLink_ViewWorksAndEveryResponseIsRefusedBeforeAnyEffect_WhileANormalQuoteIsAnswered()
    {
        var demo = await database.SeedQuoteWorldAsync();
        var normal = await database.SeedQuoteWorldAsync();
        await using var seeding = RequestsHost.Create(database);
        var (demoOwner, _) = await seeding.SignInAsync(database, demo.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (normalOwner, _) = await seeding.SignInAsync(database, normal.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var demoQuote = await QuoteLinkApi.SendAsync(database, seeding, demo, demoOwner);
        var normalQuote = await QuoteLinkApi.SendAsync(database, seeding, normal, normalOwner);
        var sender = new RecordingEmailSender();
        var client = CreateClient(sender, new FakePaymentGateway(), demo.Org);
        var optional = QuoteLinkApi.OptionalIds(await QuoteLinkApi.ViewAsync(seeding, demoQuote.Token));
        var statusBefore = await database.QuoteStatusAsync(demoQuote.QuoteId);

        // Authorized reads keep working through the token.
        var view = await PostQuoteAsync(client, "view", QuoteLinkApi.Body(demoQuote.Token));
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostQuoteAsync(client, "pdf", QuoteLinkApi.Body(demoQuote.Token))).StatusCode);

        // Approve, decline and a question: refused, nothing stored, no notification, no email.
        var approve = QuoteLinkApi.Body(demoQuote.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids(optional[0])), ("acceptTerms", true));
        var decline = QuoteLinkApi.Body(demoQuote.Token, ("reason", "Too expensive"));
        var question = QuoteLinkApi.Body(demoQuote.Token, ("message", "Can you do Friday?"));

        foreach (var (action, body) in new[] { ("approve", approve), ("decline", decline), ("clarification", question) })
        {
            var refused = await PostQuoteAsync(client, action, body);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains(DemoCode, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(0, await database.ResponseCountAsync(demoQuote.QuoteId));
        Assert.Equal(statusBefore, await database.QuoteStatusAsync(demoQuote.QuoteId));
        Assert.Equal(0, sender.Attempts);

        // A quote of an organization outside the demo is still answered on the very same host.
        var answered = await PostQuoteAsync(
            client, "approve", QuoteLinkApi.Body(normalQuote.Token, ("selectedOptionalLineIds", QuoteLinkApi.Ids()), ("acceptTerms", true)));
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal(1, await database.ResponseCountAsync(normalQuote.QuoteId, "approved"));
    }

    [Fact]
    public async Task DemoInvoiceLinkAndWebhook_ReadsWorkWritesAndEventsAreIgnoredWhileANormalInvoiceIsPaid()
    {
        var demo = await database.SeedWorldAsync();
        var normal = await database.SeedWorldAsync();
        await using var seeding = OnlineHost.Create(database);
        var (_, demoMember) = await seeding.SignInAsync(database, demo.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var (_, normalMember) = await seeding.SignInAsync(database, normal.Org, CompanySettingsDatabaseFixture.OwnerRoleId);
        var demoInvoice = await database.InvoiceAsync(demo, demoMember.UserId);
        var normalInvoice = await database.InvoiceAsync(normal, normalMember.UserId);

        // An attempt that already existed before the demo mode was enabled, so the webhook has something to match.
        var (attempt, intent) = await OnlineApi.StartCardAsync(seeding.Client, demoInvoice);
        var gateway = new FakePaymentGateway();
        var sender = new RecordingEmailSender();
        var client = CreateClient(sender, gateway, demo.Org);
        var before = await database.FlowStateAsync(demo.Org, demoInvoice.Id);

        // Reads and downloads: the page, the payment status and the PDF.
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.ViewAsync(client, demoInvoice.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.StatusAsync(client, demoInvoice.Token, attempt)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.PostAsync(client, "pdf", new JsonObject { ["token"] = demoInvoice.Token })).StatusCode);

        // Writes: refused before any attempt row, provider intent, email or review.
        var refusals = new[]
        {
            await OnlineApi.CardIntentAsync(client, demoInvoice.Token),
            await OnlineApi.PostAsync(client, "payments/bank-transfer-notice", OnlineApi.KeyBody(demoInvoice.Token)),
            await OnlineApi.PostAsync(
                client, "review", new JsonObject { ["token"] = demoInvoice.Token, ["rating"] = 5, ["comment"] = "Great" }),
        };

        foreach (var refusal in refusals)
        {
            Assert.Equal(HttpStatusCode.Forbidden, refusal.StatusCode);
            Assert.Contains(DemoCode, await refusal.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Empty(gateway.Requests);
        Assert.Equal(0, sender.Attempts);

        // The webhook keeps its signature check, then acknowledges and ignores events of the protected organization.
        var eventId = NewEvent();
        var payload = OnlineApi.Succeeded(eventId, intent, 357.28m);
        Assert.Equal(HttpStatusCode.BadRequest, (await OnlineApi.WebhookAsync(client, payload, "t=1,v1=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(client, payload)).StatusCode);
        Assert.Equal(0, await database.EventCountAsync(eventId));
        Assert.Empty(gateway.Canceled);
        Assert.Equal(before, await database.FlowStateAsync(demo.Org, demoInvoice.Id));

        // An invoice of an organization outside the demo is paid through the same host and webhook.
        var (_, normalIntent) = await OnlineApi.StartCardAsync(client, normalInvoice);
        var normalEvent = NewEvent();
        Assert.Equal(HttpStatusCode.OK, (await OnlineApi.DeliverAsync(client, OnlineApi.Succeeded(normalEvent, normalIntent, 357.28m))).StatusCode);
        Assert.Equal(1, await database.EventCountAsync(normalEvent));
        Assert.Equal(1, await database.CountAsync("payments", "organization_id = @o", ("o", normal.Org)));
        Assert.Equal(0, await database.CountAsync("payments", "organization_id = @o", ("o", demo.Org)));
    }

    [Fact]
    public async Task Registration_StaysOpenAndANewOrganizationOperatesFullyWhileDemoModeIsOn()
    {
        var demo = await database.SeedWorldAsync();
        var client = CreateClient(new RecordingEmailSender(), new FakePaymentGateway(), demo.Org);
        var email = OrganizationRegistrationApi.NewEmail();
        const string password = "correct horse battery";

        var registered = await OrganizationRegistrationApi.PostAsync(client, OrganizationRegistrationApi.ValidBody(email, password));
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var cookie = SessionApi.GetIssuedCookie(await SessionApi.SignInAsync(client, email, password));
        var session = await CompanySettingsApi.GetAsync(client, "/sessions/current", cookie);
        Assert.Null(JsonNode.Parse(await session.Content.ReadAsStringAsync())!["demoReadOnly"]);

        // The new owner writes normally: the request reaches validation instead of the demo guard.
        var write = await CompanySettingsApi.SendRawAsync(client, HttpMethod.Post, "/branches", "{}", cookie);
        Assert.NotEqual(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.DoesNotContain(DemoCode, await write.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private HttpClient CreateClient(RecordingEmailSender sender, FakePaymentGateway gateway, params Guid[] demoOrganizations)
    {
        var factory = FieldOpsApiFactory.Create(connectionString: database.ConnectionString)
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("DemoMode:Enabled", "true");

                for (var i = 0; i < demoOrganizations.Length; i++)
                {
                    builder.UseSetting($"DemoMode:OrganizationSlugs:{i}", $"org-{demoOrganizations[i]:N}");
                }

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IEmailSender>();
                    services.AddSingleton<IEmailSender>(sender);
                    services.RemoveAll<IPaymentGateway>();
                    services.AddSingleton<IPaymentGateway>(gateway);
                });
            });

        return SessionApi.CreateClient(factory);
    }

    private static Task<HttpResponseMessage> PostQuoteAsync(HttpClient client, string action, JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{QuoteLinkApi.Base}/{action}")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(TestClientIpStartupFilter.HeaderName, SessionApi.NewClientIp());

        return client.SendAsync(request);
    }
}
