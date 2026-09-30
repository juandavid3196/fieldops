using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.IntegrationTests.Api;

/// <summary>
/// Hosts the real API pipeline with explicit configuration so tests never
/// depend on developer user-secrets or CI environment variables.
/// </summary>
public sealed class FieldOpsApiFactory(
    string environment,
    IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
{
    public const string TestingEnvironment = "Testing";

    public const string AllowedOrigin = "https://app.fieldops.test";

    // Port 1 refuses connections immediately, so the check fails fast.
    public const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=fieldops;Username=fieldops;Password=not-a-secret;Timeout=3";

    public static FieldOpsApiFactory Create(
        string environment = TestingEnvironment,
        string? connectionString = UnreachableConnectionString,
        params string[] allowedOrigins)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:FieldOpsDatabase"] = connectionString ?? string.Empty,
        };

        var origins = allowedOrigins.Length == 0 ? [AllowedOrigin] : allowedOrigins;

        for (var i = 0; i < origins.Length; i++)
        {
            settings[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }

        return new FieldOpsApiFactory(environment, settings);
    }

    public static FieldOpsApiFactory CreateWithoutCorsOrigins() =>
        new(
            TestingEnvironment,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FieldOpsDatabase"] = UnreachableConnectionString,
            });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);

        // Valid SMTP settings so start-up validation passes; no test sends
        // mail because the email port is replaced or never reached.
        // Production only accepts Resend (BR-17); the key is a placeholder and no test sends.
        var production = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
        builder.UseSetting("Email:Provider", production ? "Resend" : "Smtp");
        builder.UseSetting("Email:Resend:ApiKey", production ? "not-a-real-key" : null);
        builder.UseSetting("Email:SenderAddress", "no-reply@fieldops.test");
        builder.UseSetting("Email:SenderName", "FieldOps");
        builder.UseSetting("Email:Smtp:Host", "localhost");
        builder.UseSetting("Email:Smtp:Port", "1025");
        builder.UseSetting("Email:Smtp:EnableSsl", "false");

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services
                .AddControllers()
                .AddApplicationPart(typeof(FieldOpsApiFactory).Assembly);

            // Keys live in memory per host: nothing is written to the
            // developer profile and each factory has its own key ring.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
        });
    }
}
