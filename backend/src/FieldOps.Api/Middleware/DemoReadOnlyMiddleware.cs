using System.Diagnostics;
using FieldOps.Api.Authentication;
using FieldOps.Application.Features.DemoMode;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FieldOps.Api.Middleware;

/// <summary>
/// Read-only demo mode. For a request that is authenticated as an internal or portal account of a configured demo
/// organization, every method that is not GET/HEAD/OPTIONS is answered 403 <c>demo_read_only</c> before the controller
/// runs (no handler, no email, no payment, no write), unless the action is marked <see cref="DemoReadOnlySafeAttribute"/>.
/// Authorization still decides first, so each account keeps exactly the screens its role allows. Scope comes from the
/// revalidated session and server configuration; no client value is read. Anonymous actions are not tied to an account and
/// guard themselves (password recovery, public form).
/// </summary>
public sealed class DemoReadOnlyMiddleware(RequestDelegate next)
{
    private const string PortalPath = "/portal";

    public async Task InvokeAsync(HttpContext context, IDemoProtection demo)
    {
        if (demo.Enabled && !IsReadMethod(context.Request.Method) && await IsBlockedAsync(context, demo))
        {
            await WriteRefusalAsync(context);

            return;
        }

        await next(context);
    }

    private static bool IsReadMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

    private static async Task<bool> IsBlockedAsync(HttpContext context, IDemoProtection demo)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is null || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return false;
        }

        var cancellationToken = context.RequestAborted;
        var credentials = endpoint.Metadata.GetMetadata<DemoCredentialsAttribute>() is not null;
        var safe = endpoint.Metadata.GetMetadata<DemoReadOnlySafeAttribute>() is not null;

        if (SessionCookieEvents.GetValidatedSession(context) is { } session)
        {
            if (credentials && await demo.IsProtectedUserAsync(session.User.Id, cancellationToken))
            {
                return true;
            }

            return !safe && await demo.IsProtectedOrganizationAsync(session.Organization.Id, cancellationToken);
        }

        if (!context.Request.Path.StartsWithSegments(PortalPath, StringComparison.OrdinalIgnoreCase)
            || safe
            || PortalSessionCookieEvents.GetValidatedSession(context) is not { } portal)
        {
            return false;
        }

        return await demo.IsProtectedOrganizationAsync(portal.Scope.OrganizationId, cancellationToken);
    }

    private static Task WriteRefusalAsync(HttpContext context)
    {
        var problem = new ProblemDetails { Status = StatusCodes.Status403Forbidden, Title = DemoModeMessages.Title };
        problem.Extensions["code"] = DemoModeMessages.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.CacheControl = "no-store";

        return context.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}
