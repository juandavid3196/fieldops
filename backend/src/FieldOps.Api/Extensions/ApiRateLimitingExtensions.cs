using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Extensions;

public static class ApiRateLimitingExtensions
{
    public const string SignInPolicy = "sign-in";

    public const string OrganizationRegistrationPolicy = "organization-registration";

    public const int SignInPerClientPermitLimit = 10;

    public const int SignInGlobalPermitLimit = 300;

    public static readonly TimeSpan SignInPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan SignInGlobalWindow = TimeSpan.FromMinutes(1);

    public const int OrganizationRegistrationPerClientPermitLimit = 5;

    public const int OrganizationRegistrationGlobalPermitLimit = 100;

    public static readonly TimeSpan OrganizationRegistrationPerClientWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan OrganizationRegistrationGlobalWindow = TimeSpan.FromHours(1);

    private const string SignInGlobalPartition = "sign-in-global";

    private const string OrganizationRegistrationGlobalPartition = "organization-registration-global";

    /// <summary>
    /// In-memory rate limits per named policy: per client IP (RemoteIpAddress)
    /// and one global window each. Every request counts, whatever its
    /// outcome. Rejections are 429 with Retry-After and an empty body that
    /// UseStatusCodePages turns into ProblemDetails.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SignInPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, SignInPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = SignInPerClientPermitLimit,
                        Window = SignInPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(OrganizationRegistrationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, OrganizationRegistrationPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = OrganizationRegistrationPerClientPermitLimit,
                        Window = OrganizationRegistrationPerClientWindow,
                        QueueLimit = 0,
                    }));

            // Applies only to endpoints using one of the named policies above.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                GetEndpointPolicyName(httpContext) switch
                {
                    SignInPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        SignInGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = SignInGlobalPermitLimit,
                            Window = SignInGlobalWindow,
                            QueueLimit = 0,
                        }),
                    OrganizationRegistrationPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        OrganizationRegistrationGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = OrganizationRegistrationGlobalPermitLimit,
                            Window = OrganizationRegistrationGlobalWindow,
                            QueueLimit = 0,
                        }),
                    _ => RateLimitPartition.GetNoLimiter(string.Empty),
                });

            options.OnRejected = (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? value
                    : SignInGlobalWindow;

                var seconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
                context.HttpContext.Response.Headers.RetryAfter =
                    seconds.ToString(CultureInfo.InvariantCulture);

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    private static string? GetEndpointPolicyName(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static string GetClientPartitionKey(HttpContext httpContext, string policyName)
    {
        var address = httpContext.Connection.RemoteIpAddress;

        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        // Prefixed so the two endpoints never share a per-client partition.
        return $"{policyName}:{address?.ToString() ?? IPAddress.None.ToString()}";
    }
}
