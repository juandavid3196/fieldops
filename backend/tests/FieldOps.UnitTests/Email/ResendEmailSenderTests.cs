using System.Net;
using System.Text.Json.Nodes;
using FieldOps.Application.Features.Email;
using FieldOps.Infrastructure;
using FieldOps.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace FieldOps.UnitTests.Email;

/// <summary>AC-15: Resend request shape and failure mapping with a fake handler.</summary>
public class ResendEmailSenderTests
{
    private const string ApiKey = "re_test_key_do_not_log";

    private static readonly EmailMessage Message =
        new("alex@example.com", "Reset your FieldOps password", "plain body text", "<p>html body</p>");

    [Fact]
    public async Task SendAsync_PostsBearerRequestOnSuccessAndThrowsLoggingOnlyProviderAndStatusOnFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.OK);
        var logs = new CapturedLogs();
        var sender = BuildSender(handler, logs);

        await sender.SendAsync(Message, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.resend.com/emails", request.Uri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);

        var body = JsonNode.Parse(request.Body)!.AsObject();
        Assert.Equal("FieldOps <no-reply@fieldops.test>", body["from"]!.GetValue<string>());
        Assert.Equal(["alex@example.com"], body["to"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray());
        Assert.Equal("Reset your FieldOps password", body["subject"]!.GetValue<string>());
        Assert.Equal("plain body text", body["text"]!.GetValue<string>());
        Assert.Equal("<p>html body</p>", body["html"]!.GetValue<string>());
        Assert.Empty(logs.Entries);

        foreach (var status in new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.InternalServerError })
        {
            var failing = BuildSender(new FakeHandler(status), logs);

            var failure = await Assert.ThrowsAnyAsync<Exception>(() => failing.SendAsync(Message, CancellationToken.None));

            Assert.Contains(((int)status).ToString(), failure.Message, StringComparison.Ordinal);
        }

        var transport = BuildSender(new FakeHandler(null), logs);
        await Assert.ThrowsAnyAsync<Exception>(() => transport.SendAsync(Message, CancellationToken.None));

        Assert.Equal(3, logs.Entries.Count);
        Assert.Contains("Resend", logs.Entries[0], StringComparison.Ordinal);
        Assert.Contains("422", logs.Entries[0], StringComparison.Ordinal);
        Assert.Contains("500", logs.Entries[1], StringComparison.Ordinal);

        var allLogs = string.Join('\n', logs.Entries);
        Assert.DoesNotContain(ApiKey, allLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("alex@example.com", allLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("plain body text", allLogs, StringComparison.Ordinal);
    }

    private static IEmailSender BuildSender(FakeHandler handler, CapturedLogs logs)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:FieldOpsDatabase"] = "Host=localhost;Database=unused",
            ["Email:Provider"] = "Resend",
            ["Email:SenderAddress"] = "no-reply@fieldops.test",
            ["Email:SenderName"] = "FieldOps",
            ["Email:Resend:ApiKey"] = ApiKey,
        };

        var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(logs));
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddInfrastructure(new Configuration(values));
        services.AddKeyedSingleton(EmailServiceCollectionExtensions.ResendHttpClientKey, (_, _) => new HttpClient(handler));

        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string? Authorization, string Body);

    // A null status simulates a transport error.
    private sealed class FakeHandler(HttpStatusCode? status) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));

            return status is { } code
                ? new HttpResponseMessage(code) { Content = new StringContent("provider said: alex@example.com") }
                : throw new HttpRequestException("connection refused for alex@example.com");
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capture(Entries);

        public void Dispose()
        {
        }

        private sealed class Capture(List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Add($"{formatter(state, exception)} {exception}");
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "FieldOps.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Configuration(IReadOnlyDictionary<string, string?> values, string path = "")
        : IConfigurationRoot, IConfigurationSection
    {
        public string Key => path.Split(':')[^1];

        public string Path => path;

        public string? Value
        {
            get => values.GetValueOrDefault(path);
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationProvider> Providers => [];

        public string? this[string key]
        {
            get => values.GetValueOrDefault(Combine(key));
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Configuration(values, Combine(key));

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        public void Reload()
        {
        }

        private string Combine(string key) => path.Length == 0 ? key : $"{path}:{key}";
    }
}
