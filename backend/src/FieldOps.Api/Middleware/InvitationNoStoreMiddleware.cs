namespace FieldOps.Api.Middleware;

/// <summary>
/// Every response under <c>/invitations</c> carries <c>Cache-Control: no-store</c>
/// (BR-13), including rate-limit, authentication and media-type rejections
/// that never reach the controller.
/// </summary>
public sealed class InvitationNoStoreMiddleware(RequestDelegate next)
{
    private const string InvitationsPath = "/invitations";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments(InvitationsPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return next(context);
    }
}
