using FieldOps.Api.Configuration;
using FieldOps.Api.Middleware;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace FieldOps.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public const string SelfHealthCheckName = "self";

    public static IServiceCollection AddApiErrorHandling(
        this IServiceCollection services)
    {
        services.AddProblemDetails();

        // Handlers run in registration order; the global handler is last.
        services.AddExceptionHandler<BadHttpRequestExceptionHandler>();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    public static IServiceCollection AddApiCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<CorsSettings>()
            .Bind(configuration.GetSection(CorsSettings.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CorsSettings>, CorsSettingsValidator>();

        services.AddCors();
        services
            .AddOptions<CorsOptions>()
            .Configure<IOptions<CorsSettings>>((cors, settings) =>
                cors.AddPolicy(CorsSettings.PolicyName, policy => policy
                    .WithOrigins(settings.Value.AllowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .WithExposedHeaders(HeaderNames.RetryAfter)));

        return services;
    }

    public static IServiceCollection AddApiHealthChecks(
        this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck(SelfHealthCheckName, () => HealthCheckResult.Healthy());

        return services;
    }
}
