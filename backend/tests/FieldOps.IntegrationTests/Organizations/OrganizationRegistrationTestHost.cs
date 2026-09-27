using FieldOps.IntegrationTests.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FieldOps.IntegrationTests.Organizations;

/// <summary>
/// One API host (own rate limiter and Data Protection keys) with optional
/// log capture, mirroring <c>SessionTestHost</c>.
/// </summary>
public sealed class OrganizationRegistrationTestHost : IAsyncDisposable
{
    private readonly FieldOpsApiFactory _baseFactory;

    private OrganizationRegistrationTestHost(
        FieldOpsApiFactory baseFactory,
        WebApplicationFactory<Program> factory,
        CapturingLoggerProvider? logs)
    {
        _baseFactory = baseFactory;
        Factory = factory;
        Logs = logs;
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Client { get; }

    public CapturingLoggerProvider? Logs { get; }

    public static OrganizationRegistrationTestHost Create(
        string? connectionString = null,
        bool captureLogs = false)
    {
        var baseFactory = FieldOpsApiFactory.Create(
            connectionString: connectionString ?? FieldOpsApiFactory.UnreachableConnectionString);

        WebApplicationFactory<Program> factory = baseFactory;
        CapturingLoggerProvider? logs = null;

        if (captureLogs)
        {
            logs = new CapturingLoggerProvider();
            factory = logs.AttachTo(factory);
        }

        return new OrganizationRegistrationTestHost(baseFactory, factory, logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        // Disposing the base factory also disposes derived factories.
        await _baseFactory.DisposeAsync();
    }
}
