using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FieldOps.Api.Extensions;

public static class ApiRateLimitingExtensions
{
    public const string SignInPolicy = "sign-in";

    public const string OrganizationRegistrationPolicy = "organization-registration";

    /// <summary>GET /public/organizations/{slug}/service-request-form (public request BR-16).</summary>
    public const string PublicRequestFormPolicy = "public-request-form";

    /// <summary>POST /public/organizations/{slug}/service-requests (public request BR-16).</summary>
    public const string PublicRequestSubmitPolicy = "public-request-submit";

    public const int PublicRequestFormPerClientPermitLimit = 60;

    public const int PublicRequestFormGlobalPermitLimit = 600;

    public const int PublicRequestSubmitPerClientPermitLimit = 5;

    public const int PublicRequestSubmitGlobalPermitLimit = 100;

    public static readonly TimeSpan PublicRequestFormPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan PublicRequestFormGlobalWindow = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan PublicRequestSubmitPerClientWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan PublicRequestSubmitGlobalWindow = TimeSpan.FromHours(1);

    private const string PublicRequestFormGlobalPartition = "public-request-form-global";

    private const string PublicRequestSubmitGlobalPartition = "public-request-submit-global";

    /// <summary>view, calculate, photos and logo of the public quote link (customer-quote-approval BR-20).</summary>
    public const string QuoteLinkReadPolicy = "quote-link-read";

    /// <summary>approve, decline and clarification of the public quote link.</summary>
    public const string QuoteLinkActionPolicy = "quote-link-action";

    /// <summary>pdf of the public quote link.</summary>
    public const string QuoteLinkPdfPolicy = "quote-link-pdf";

    public const int QuoteLinkReadPerClientPermitLimit = 60;

    public const int QuoteLinkReadGlobalPermitLimit = 600;

    public const int QuoteLinkActionPerClientPermitLimit = 10;

    public const int QuoteLinkActionGlobalPermitLimit = 100;

    public const int QuoteLinkPdfPerClientPermitLimit = 10;

    public const int QuoteLinkPdfGlobalPermitLimit = 100;

    public static readonly TimeSpan QuoteLinkReadPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan QuoteLinkReadGlobalWindow = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan QuoteLinkActionPerClientWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan QuoteLinkActionGlobalWindow = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan QuoteLinkPdfPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan QuoteLinkPdfGlobalWindow = TimeSpan.FromMinutes(1);

    private const string QuoteLinkReadGlobalPartition = "quote-link-read-global";

    private const string QuoteLinkActionGlobalPartition = "quote-link-action-global";

    private const string QuoteLinkPdfGlobalPartition = "quote-link-pdf-global";

    /// <summary>Shared by validate, accept and accept-existing (invitation BR-12).</summary>
    public const string InvitationPolicy = "invitation";

    /// <summary>POST /password-resets: 5 per client per 15 minutes (password recovery BR-12).</summary>
    public const string PasswordResetRequestPolicy = "password-reset-request";

    /// <summary>Validate and confirm share one per-client partition: 20 per 5 minutes.</summary>
    public const string PasswordResetTokenPolicy = "password-reset-token";

    public const int PasswordResetRequestPerClientPermitLimit = 5;

    public const int PasswordResetTokenPerClientPermitLimit = 20;

    /// <summary>One window across the three password reset endpoints.</summary>
    public const int PasswordResetGlobalPermitLimit = 300;

    public static readonly TimeSpan PasswordResetRequestPerClientWindow = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan PasswordResetTokenPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan PasswordResetGlobalWindow = TimeSpan.FromMinutes(1);

    private const string PasswordResetGlobalPartition = "password-reset-global";

    public const int InvitationPerClientPermitLimit = 20;

    public const int InvitationGlobalPermitLimit = 300;

    public static readonly TimeSpan InvitationPerClientWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan InvitationGlobalWindow = TimeSpan.FromMinutes(1);

    private const string InvitationGlobalPartition = "invitation-global";

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

            // One partition per client shared by the three invitation endpoints.
            options.AddPolicy(InvitationPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, InvitationPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = InvitationPerClientPermitLimit,
                        Window = InvitationPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(PasswordResetRequestPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, PasswordResetRequestPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PasswordResetRequestPerClientPermitLimit,
                        Window = PasswordResetRequestPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(PasswordResetTokenPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, PasswordResetTokenPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PasswordResetTokenPerClientPermitLimit,
                        Window = PasswordResetTokenPerClientWindow,
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

            options.AddPolicy(PublicRequestFormPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, PublicRequestFormPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PublicRequestFormPerClientPermitLimit,
                        Window = PublicRequestFormPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(PublicRequestSubmitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, PublicRequestSubmitPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PublicRequestSubmitPerClientPermitLimit,
                        Window = PublicRequestSubmitPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(QuoteLinkReadPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, QuoteLinkReadPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = QuoteLinkReadPerClientPermitLimit,
                        Window = QuoteLinkReadPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(QuoteLinkActionPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, QuoteLinkActionPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = QuoteLinkActionPerClientPermitLimit,
                        Window = QuoteLinkActionPerClientWindow,
                        QueueLimit = 0,
                    }));

            options.AddPolicy(QuoteLinkPdfPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartitionKey(httpContext, QuoteLinkPdfPolicy),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = QuoteLinkPdfPerClientPermitLimit,
                        Window = QuoteLinkPdfPerClientWindow,
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
                    InvitationPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        InvitationGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = InvitationGlobalPermitLimit,
                            Window = InvitationGlobalWindow,
                            QueueLimit = 0,
                        }),
                    // The three password reset endpoints share one global partition.
                    PasswordResetRequestPolicy or PasswordResetTokenPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        PasswordResetGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PasswordResetGlobalPermitLimit,
                            Window = PasswordResetGlobalWindow,
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
                    PublicRequestFormPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        PublicRequestFormGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PublicRequestFormGlobalPermitLimit,
                            Window = PublicRequestFormGlobalWindow,
                            QueueLimit = 0,
                        }),
                    PublicRequestSubmitPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        PublicRequestSubmitGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = PublicRequestSubmitGlobalPermitLimit,
                            Window = PublicRequestSubmitGlobalWindow,
                            QueueLimit = 0,
                        }),
                    QuoteLinkReadPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        QuoteLinkReadGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = QuoteLinkReadGlobalPermitLimit,
                            Window = QuoteLinkReadGlobalWindow,
                            QueueLimit = 0,
                        }),
                    QuoteLinkActionPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        QuoteLinkActionGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = QuoteLinkActionGlobalPermitLimit,
                            Window = QuoteLinkActionGlobalWindow,
                            QueueLimit = 0,
                        }),
                    QuoteLinkPdfPolicy => RateLimitPartition.GetFixedWindowLimiter(
                        QuoteLinkPdfGlobalPartition,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = QuoteLinkPdfGlobalPermitLimit,
                            Window = QuoteLinkPdfGlobalWindow,
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
