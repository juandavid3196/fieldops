using FieldOps.Api.Middleware;
using FieldOps.Application.Authentication;
using FieldOps.Application.Features.DemoMode;
using FieldOps.Application.Features.PortalAccess;
using FieldOps.Application.Features.PortalAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace FieldOps.IntegrationTests.DemoMode;

/// <summary>Read-only demo classification without a database: which requests are refused before the controller runs.</summary>
public class DemoReadOnlyMiddlewareTests
{
    private static readonly Guid DemoOrg = Guid.NewGuid();

    private static readonly Guid DemoUser = Guid.NewGuid();

    private static readonly Guid OtherOrg = Guid.NewGuid();

    [Theory]
    [InlineData("GET", Kind.None, true, false)]
    [InlineData("POST", Kind.None, true, true)]
    [InlineData("PUT", Kind.None, true, true)]
    [InlineData("PATCH", Kind.None, true, true)]
    [InlineData("DELETE", Kind.None, true, true)]
    [InlineData("POST", Kind.Safe, true, false)]
    [InlineData("POST", Kind.Anonymous, true, false)]
    [InlineData("POST", Kind.None, false, false)]
    public async Task Internal_Request_IsRefusedOnlyWhenItWritesAsADemoAccount(string method, Kind kind, bool demoOrganization, bool refused)
    {
        var context = Context(method, "/branches", kind, internalOrg: demoOrganization ? DemoOrg : OtherOrg);

        var reachedController = await RunAsync(context);

        Assert.Equal(refused, !reachedController);
        Assert.Equal(refused ? StatusCodes.Status403Forbidden : StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Portal_Write_IsRefusedForADemoCustomerButSafeActionsPass()
    {
        var write = Context("POST", "/portal/properties", Kind.None, portalOrg: DemoOrg);
        var calculate = Context("POST", "/portal/quotes/x/calculate", Kind.Safe, portalOrg: DemoOrg);
        var ordinary = Context("POST", "/portal/properties", Kind.None, portalOrg: OtherOrg);

        Assert.False(await RunAsync(write));
        Assert.True(await RunAsync(calculate));
        Assert.True(await RunAsync(ordinary));
        Assert.Equal(StatusCodes.Status403Forbidden, write.Response.StatusCode);
    }

    [Fact]
    public async Task CredentialAction_IsRefusedForADemoUserEvenInAnotherOrganization()
    {
        var context = Context("POST", "/account/password", Kind.Credentials, internalOrg: OtherOrg);

        Assert.False(await RunAsync(context));
    }

    [Fact]
    public async Task DisabledMode_NeverRefuses()
    {
        var context = Context("DELETE", "/branches/x", Kind.None, internalOrg: DemoOrg);

        Assert.True(await RunAsync(context, enabled: false));
    }

    public enum Kind
    {
        None,
        Safe,
        Anonymous,
        Credentials,
    }

    private static async Task<bool> RunAsync(DefaultHttpContext context, bool enabled = true)
    {
        var reached = false;
        var middleware = new DemoReadOnlyMiddleware(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, new FakeDemo(enabled));

        return reached;
    }

    private static DefaultHttpContext Context(string method, string path, Kind kind, Guid? internalOrg = null, Guid? portalOrg = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Response.StatusCode = StatusCodes.Status200OK;

        object[] metadata = kind switch
        {
            Kind.Safe => [new DemoReadOnlySafeAttribute()],
            Kind.Anonymous => [new AllowAnonymousAttribute()],
            Kind.Credentials => [new DemoCredentialsAttribute()],
            _ => [],
        };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));

        if (internalOrg is { } org)
        {
            // The key SessionCookieEvents stores the revalidated session under.
            context.Items["FieldOps.Session"] = new SessionView(
                new SessionUser(DemoUser, "Ada", "Lovelace", "ada@example.com", null, "en", "light"),
                new SessionOrganization(org, "Org"),
                new SessionRole("owner", "Owner"));
        }

        if (portalOrg is { } portal)
        {
            var scope = new PortalScope(portal, Guid.NewGuid(), Guid.NewGuid(), DemoUser);
            context.Items["FieldOps.PortalSession"] = new PortalSessionContext(
                new PortalSessionView(
                    new PortalSessionUser("Carla", "Customer", "carla@example.com", "en", "light"),
                    new PortalSessionAccount(scope.ContactId, "Carla", "Org", false),
                    []),
                scope);
        }

        return context;
    }

    private sealed class FakeDemo(bool enabled) : IDemoProtection
    {
        public bool Enabled => enabled;

        public bool IsProtectedSlug(string slug) => false;

        public Task<bool> IsProtectedOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(organizationId == DemoOrg);

        public Task<bool> IsProtectedUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == DemoUser);
    }
}
