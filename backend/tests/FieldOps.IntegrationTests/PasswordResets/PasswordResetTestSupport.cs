using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldOps.Application.Features.Email;
using FieldOps.IntegrationTests.Api;
using FieldOps.IntegrationTests.CompanySettings;
using FieldOps.IntegrationTests.Sessions;
using FieldOps.IntegrationTests.Users;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FieldOps.IntegrationTests.PasswordResets;

/// <summary>Records sent messages; <see cref="Fail"/> makes every send throw (like a provider outage).</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _messages = [];

    private int _attempts;

    public bool Fail { get; set; }

    public int Attempts => Volatile.Read(ref _attempts);

    public IReadOnlyList<EmailMessage> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);

        if (Fail)
        {
            throw new InvalidOperationException("provider down for " + message.To);
        }

        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>Waits for the background dispatcher to attempt <paramref name="count"/> sends.</summary>
    public async Task WaitForAttemptsAsync(int count)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (Attempts < count)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Expected {count} send attempts but saw {Attempts}.");
            await Task.Delay(20);
        }
    }

    /// <summary>The raw token in the reset link of a delivered message.</summary>
    public static string TokenOf(EmailMessage message) =>
        Regex.Match(message.TextBody, "#token=([A-Za-z0-9_-]{43})(?![A-Za-z0-9_-])").Groups[1].Value;
}

/// <summary>
/// One API host (own rate limiter, throttles and dispatcher queue) with a
/// controllable clock, a recording email port and optional log capture.
/// </summary>
public sealed class PasswordResetHost : IAsyncDisposable
{
    private static readonly Regex Token43 = new("^[A-Za-z0-9_-]{43}$");

    private readonly SessionTestHost _host;

    private PasswordResetHost(SessionTestHost host, HttpClient client, RecordingEmailSender sender)
    {
        _host = host;
        Client = client;
        Sender = sender;
    }

    public HttpClient Client { get; }

    public RecordingEmailSender Sender { get; }

    public MutableTimeProvider Time => _host.Time!;

    public CapturingLoggerProvider? Logs => _host.Logs;

    public static PasswordResetHost Create(CompanySettingsDatabaseFixture database, bool captureLogs = false)
    {
        var host = SessionTestHost.Create(
            database.ConnectionString,
            new MutableTimeProvider(DateTimeOffset.UtcNow),
            captureLogs);
        var sender = new RecordingEmailSender();

        var factory = host.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(sender);
        }));

        return new PasswordResetHost(host, SessionApi.CreateClient(factory), sender);
    }

    public static bool IsWellFormedToken(string token) => Token43.IsMatch(token);

    public Task<HttpResponseMessage> RequestAsync(string email, string? ip = null) =>
        PostAsync("/password-resets", new JsonObject { ["email"] = email }, ip);

    public Task<HttpResponseMessage> ValidateAsync(string token, string? ip = null) =>
        PostAsync("/password-resets/validate", new JsonObject { ["token"] = token }, ip);

    public Task<HttpResponseMessage> ConfirmAsync(string token, string password, string? ip = null) =>
        PostAsync("/password-resets/confirm", new JsonObject { ["token"] = token, ["password"] = password }, ip);

    public Task<HttpResponseMessage> PostAsync(string path, JsonObject body, string? ip = null) =>
        PostRawAsync(path, body.ToJsonString(), ip: ip);

    public Task<HttpResponseMessage> PostRawAsync(
        string path, string? body, string? mediaType = "application/json", string? ip = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = SessionApi.CreateContent(body, mediaType);
        }

        return SessionApi.SendAsync(Client, request, ip, null, origin, null);
    }

    public Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        SessionApi.SignInAsync(Client, email, password);

    public async Task<string> SignInCookieAsync(string email, string password = CompanySettingsDatabaseFixture.Password) =>
        SessionApi.GetIssuedCookie(await SignInAsync(email, password));

    public Task<HttpResponseMessage> PostWithCookieAsync(string path, string? cookie, JsonObject? body = null) =>
        CompanySettingsApi.PostAsync(Client, path, body, cookie);

    /// <summary>Status, headers (without Date) and body: what an anonymous caller can observe.</summary>
    public static async Task<string> SignatureAsync(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Where(header => !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase))
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => $"{header.Key}={string.Join(',', header.Value)}");

        return $"{(int)response.StatusCode}|{string.Join(';', headers)}|{await response.Content.ReadAsStringAsync()}";
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.DisposeAsync();
    }
}

public static class PasswordResetSeed
{
    /// <summary>A user of any status with the fixture password and first name "Ada".</summary>
    public static async Task<(Guid UserId, string Email)> SeedRecoveryUserAsync(
        this CompanySettingsDatabaseFixture db, string status = "active", string? email = null)
    {
        var id = Guid.NewGuid();
        var address = email ?? CompanySettingsDatabaseFixture.NewEmail();

        // status is a test-controlled constant, never user input.
        await db.ExecuteAsync(
            $"""
            INSERT INTO users (id, email, password_hash, first_name, last_name, status, email_verified_at)
            VALUES (@id, @email, @hash, 'Ada', 'Lovelace', '{status}', now())
            """,
            ("id", id),
            ("email", address),
            ("hash", db.PasswordHash));

        return (id, address);
    }

    public static string HashOf(string rawToken) => RecordingInvitationDelivery.HashOf(rawToken);

    public static Task<long> OpenTokensAsync(this CompanySettingsDatabaseFixture db, Guid userId) =>
        db.CountAsync("SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NULL", ("u", userId));

    public static Task<long> TokensAsync(this CompanySettingsDatabaseFixture db, Guid userId) =>
        db.CountAsync("SELECT COUNT(*) FROM password_reset_tokens WHERE user_id = @u", ("u", userId));

    public static Task<string> PasswordHashAsync(this CompanySettingsDatabaseFixture db, Guid userId) =>
        db.TextAsync("SELECT password_hash FROM users WHERE id = @u", ("u", userId));

    public static async Task<JsonObject> ReadObjectAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    public static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
}
