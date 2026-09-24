using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Extensions;

public static class ApiRateLimitingExtensions
{
    public const string SignInPolicy = "sign-in";

    public const int SignInPerClientPermitLimit = 10;

    public const int SignInGlobalPermitLimit = 300;

    public static readonly TimeSpan SignInPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan SignInGlobalWindow = TimeSpan.FromMinutes(1);

    private const string SignInGlobalPartition = "sign-in-global";

    /// <summary>
    /// In-memory rate limits for POST /sessions: per client IP
    /// (RemoteIpAddress) and one global window. Every request counts,
    /// whatever its outcome. Rejections are 429 with Retry-After and an
    /// empty body that UseStatusCodePages turns into ProblemDetails.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SignInPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = SignInPerClientPermitLimit,
                        Window = SignInPerClientWindow,
                        QueueLimit = 0,
                    }));

            // Applies only to endpoints using the sign-in policy.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                IsSignInEndpoint(httpContext)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        SignInGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = SignInGlobalPermitLimit,
                            Window = SignInGlobalWindow,
                            QueueLimit = 0,
                        })
                    : RateLimitPartition.GetNoLimiter(string.Empty));

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

    private static bool IsSignInEndpoint(HttpContext httpContext) =>
        httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            == SignInPolicy;

    private static string GetClientPartitionKey(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;

        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        return address?.ToString() ?? IPAddress.None.ToString();
    }
}
