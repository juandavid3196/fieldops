using FieldOps.IntegrationTests.Api;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.Sessions;

/// <summary>
/// One API host (own rate limiter, throttle and Data Protection keys) with
/// an optional controllable clock and log capture.
/// </summary>
public sealed class SessionTestHost : IAsyncDisposable
{
    private readonly FieldOpsApiFactory _baseFactory;

    private SessionTestHost(
        FieldOpsApiFactory baseFactory,
        WebApplicationFactory<Program> factory,
        MutableTimeProvider? time,
        CapturingLoggerProvider? logs)
    {
        _baseFactory = baseFactory;
        Factory = factory;
        Time = time;
        Logs = logs;
        Client = SessionApi.CreateClient(factory);
    }

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Client { get; }

    public MutableTimeProvider? Time { get; }

    public CapturingLoggerProvider? Logs { get; }

    public static SessionTestHost Create(
        string? connectionString = null,
        MutableTimeProvider? time = null,
        bool captureLogs = false)
    {
        var baseFactory = FieldOpsApiFactory.Create(
            connectionString: connectionString ?? FieldOpsApiFactory.UnreachableConnectionString);

        WebApplicationFactory<Program> factory = baseFactory;

        if (time is not null)
        {
            factory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(time)));
        }

        CapturingLoggerProvider? logs = null;

        if (captureLogs)
        {
            logs = new CapturingLoggerProvider();
            factory = logs.AttachTo(factory);
        }

        return new SessionTestHost(baseFactory, factory, time, logs);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        // Disposing the base factory also disposes derived factories.
        await _baseFactory.DisposeAsync();
    }
}

/// <summary>
/// Test clock that only moves when the test advances it.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
